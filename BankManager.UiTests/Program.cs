using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BankManager;
using BankManager.Core;

// Integração com janela real: usa o mesmo App da produção, mas força uma base temporária isolada.
var directory = Path.Combine(Path.GetTempPath(), "bankmanager-ui-" + Guid.NewGuid());
Environment.SetEnvironmentVariable("BANKMANAGER_DATA_DIR", directory);
// Assume falha até completar todo o roteiro; o CI reconhece o sucesso pelo código de saída.
int exitCode = 1;
AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().AfterSetup(builder =>
{
    var lifetime = (IClassicDesktopStyleApplicationLifetime)builder.Instance!.ApplicationLifetime!;
    // Aguarda a inicialização e executa na thread gráfica, onde os controles Avalonia podem ser manipulados.
    lifetime.Startup += (_, _) => DispatcherTimer.RunOnce(async () =>
    {
        try
        {
            var main = lifetime.MainWindow!;
            // O último elemento é o diálogo modal quando há um editor aberto; caso contrário, é a janela principal.
            Window Active() => lifetime.Windows.Last();
            // Procura o botão no conteúdo visual renderizado pelo rótulo que o usuário lê. Botões com ícone guardam o
            // rótulo no nome de automação (o mesmo que um leitor de tela anuncia), então um único critério serve aos dois tipos.
            Button FindButton(Window window, string label) => window.GetVisualDescendants().OfType<Button>().First(b => AutomationProperties.GetName(b) == label);
            // Dispara o evento do botão e dá tempo para atualizar layout e abrir/fechar diálogos.
            // Não simula coordenadas do mouse, seletores nativos de arquivos ou tecnologias assistivas.
            async Task Click(string label, Window? window = null)
            {
                FindButton(window ?? Active(), label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(180);
            }
            // Uma condição falsa interrompe o roteiro e mantém o resultado como falha.
            void Check(bool ok, string description) { if (!ok) throw new Exception(description); Console.WriteLine("PASS " + description); }
            Check(main.IsVisible, "Main window opens");
            // Cenário 1: cadastro de pessoas e rendas pela interface, incluindo o fechamento do modal ao salvar.
            await Click("Cadastrar primeira pessoa", main);
            var inputs = Active().GetVisualDescendants().OfType<TextBox>().ToArray(); inputs[0].Text = "Ana"; inputs[1].Text = "3000,00";
            await Click("Salvar pessoa");
            Check(lifetime.Windows.Count == 1, "Person dialog saves and closes");
            await Click("Pessoas e rendas", main); await Click("Adicionar pessoa", main);
            inputs = Active().GetVisualDescendants().OfType<TextBox>().ToArray(); inputs[0].Text = "Bruno"; inputs[1].Text = "1000,00"; await Click("Salvar pessoa");
            // Cenário 2: compra compartilhada com prévia proporcional de 600/200 e conferência do JSON salvo.
            await Click("Despesas", main); await Click("Nova despesa", main);
            inputs = Active().GetVisualDescendants().OfType<TextBox>().ToArray();
            inputs.First(i => i.PlaceholderText == "Ex.: Compra do mercado").Text = "Mercado da família";
            inputs.First(i => i.PlaceholderText == "Ex.: 1000,00").Text = "800,00";
            await Click("Conferir divisão antes de salvar");
            Check(Active().GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("600,00") == true && t.Text.Contains("200,00")), "Proportional preview renders 600 / 200");
            await Click("Salvar despesa");
            Check(LocalStore.Read(Path.Combine(directory, "financas.json")).Expenses.Single().Amount == 800m, "Expense persists from UI");
            // Cenário 3: mudança de situação precisa chegar ao armazenamento, não apenas à tela.
            await Click("Marcar como pago", main);
            Check(LocalStore.Read(Path.Combine(directory, "financas.json")).Expenses.Single().Paid, "Payment action persists");
            // Cenário 4: valor inválido mantém o formulário aberto e preserva o valor anteriormente salvo.
            await Click("Detalhes / editar", main);
            inputs = Active().GetVisualDescendants().OfType<TextBox>().ToArray(); inputs.First(i => i.PlaceholderText == "Ex.: 1000,00").Text = "-1";
            await Click("Salvar despesa");
            Check(lifetime.Windows.Count == 2, "Invalid expense keeps editor open");
            Check(LocalStore.Read(Path.Combine(directory, "financas.json")).Expenses.Single().Amount == 800m, "Invalid edit preserves saved value");
            await Click("Cancelar");
            // Cenário 5: duas unidades de 25,50 devem gerar uma despesa de 51,00 com a quantidade registrada.
            await Click("Nova despesa", main);
            inputs = Active().GetVisualDescendants().OfType<TextBox>().ToArray();
            inputs.First(i => i.PlaceholderText == "Ex.: Compra do mercado").Text = "Compra detalhada";
            Active().GetVisualDescendants().OfType<Expander>().Single().IsExpanded = true;
            await Task.Delay(180);
            inputs = Active().GetVisualDescendants().OfType<TextBox>().ToArray();
            inputs.First(i => i.PlaceholderText == "Nome do item").Text = "Arroz";
            inputs.First(i => i.PlaceholderText == "Quantidade").Text = "2";
            inputs.First(i => i.PlaceholderText == "Preço (R$)").Text = "25,50";
            await Click("+ Adicionar item");
            await Click("Salvar despesa");
            var itemExpense = LocalStore.Read(Path.Combine(directory, "financas.json")).Expenses.Single(e => e.Title == "Compra detalhada");
            Check(itemExpense.Amount == 51m && itemExpense.Items.Single().Quantity == 2m, "Item list calculates and persists 2 × 25.50");
            // Cenário 6: navegação renderiza as páginas de previsão e backup sem erros.
            await Click("Previsões", main);
            Check(main.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Olhe para o próximo mês"), "Forecast screen renders");
            await Click("Dados e ajuda", main);
            Check(main.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Backup e recuperação"), "Backup screen renders");
            // Cenário 7: o tema muda na hora, vai para settings.json e não toca nas finanças.
            var themeBox = main.GetVisualDescendants().OfType<ComboBox>().First(c => AutomationProperties.GetName(c) == "Tema");
            var financeBefore = File.ReadAllBytes(Path.Combine(directory, "financas.json"));
            themeBox.SelectedIndex = 2; await Task.Delay(180);
            Check(Application.Current!.RequestedThemeVariant == ThemeVariant.Dark, "Dark theme applies immediately");
            // O fundo da janela é um DynamicResource: confirma que ele realmente mudou de cor com o tema.
            var darkBg = ((ISolidColorBrush)main.Background!).Color;
            themeBox.SelectedIndex = 1; await Task.Delay(180);
            var lightBg = ((ISolidColorBrush)main.Background!).Color;
            Check(Application.Current.RequestedThemeVariant == ThemeVariant.Light && darkBg != lightBg && lightBg.R > darkBg.R, "Window background follows the theme");
            Check(SettingsStore.Load(directory).Theme == ThemePreference.Light, "Theme preference persists in settings.json");
            Check(financeBefore.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "financas.json"))), "Theme change leaves financas.json untouched");
            await Click("Visão geral", main);
            // Captura opcional para documentação; os dados exibidos são exclusivamente os dados sintéticos deste teste.
            var output = Environment.GetEnvironmentVariable("BANKMANAGER_SCREENSHOT");
            if (!string.IsNullOrEmpty(output))
            {
                // BANKMANAGER_SCREENSHOT_THEME=dark|light permite fotografar o outro tema.
                var shotTheme = Environment.GetEnvironmentVariable("BANKMANAGER_SCREENSHOT_THEME");
                if (shotTheme is "dark" or "light") { App.ApplyTheme(shotTheme == "dark" ? ThemePreference.Dark : ThemePreference.Light); await Task.Delay(300); }
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)main.Bounds.Width, (int)main.Bounds.Height), new Vector(96, 96));
                bitmap.Render(main); bitmap.Save(output, PngBitmapEncoderOptions.Default); Console.WriteLine("Screenshot: " + output);
            }
            exitCode = 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); }
        // Encerra inclusive quando um assert falha, liberando o bloqueio do armazenamento da janela.
        finally { lifetime.Shutdown(exitCode); }
    }, TimeSpan.FromSeconds(1));
}).StartWithClassicDesktopLifetime(args);
// Limpa a base temporária depois de sair do ciclo de vida gráfico; uma falha de limpeza não altera o teste.
try { Directory.Delete(directory, true); } catch (IOException) { }
return exitCode;
