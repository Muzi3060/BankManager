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
// Modo de capturas para a documentação: BANKMANAGER_SCREENSHOTS=<pasta> semeia dados de demonstração (sintéticos),
// fotografa cada página nos temas claro e escuro e encerra, sem executar o roteiro de verificação.
var shotsDirectory = Environment.GetEnvironmentVariable("BANKMANAGER_SCREENSHOTS");
if (!string.IsNullOrEmpty(shotsDirectory))
{
    using var seed = new LocalStore(directory); seed.Save(DemoData());
}
// Dados fictícios que reproduzem o desenho do aplicativo: duas pessoas, oito despesas no mês atual (total R$ 4.520,10).
static BudgetData DemoData()
{
    var first = Dates.Month(DateOnly.FromDateTime(DateTime.Today));
    var ana = new Person { Name = "Ana" }; var bruno = new Person { Name = "Bruno" };
    var data = new BudgetData { People = [ana, bruno], Incomes = [new() { PersonId = ana.Id, Month = first, Amount = 4200 }, new() { PersonId = bruno.Id, Month = first, Amount = 2800 }] };
    var group = Guid.NewGuid();
    void Add(string title, string category, int day, decimal amount, bool paid = false, int installment = 1) => data.Expenses.Add(new Expense
    {
        Title = title, Category = category, DueDate = new DateOnly(first.Year, first.Month, day), Amount = amount, Paid = paid, Split = SplitMode.AvailableIncome, Participants = [ana.Id, bruno.Id],
        InstallmentGroup = installment > 1 ? group : null, InstallmentNumber = installment, InstallmentCount = installment > 1 ? 10 : 1
    });
    Add("Aluguel", "Moradia", 5, 1800, true); Add("Plano de saúde", "Saúde", 10, 640, true); Add("Energia elétrica", "Moradia", 12, 214.90m);
    Add("Notebook", "Educação", 15, 389, installment: 3); Add("Transporte", "Transporte", 18, 380); Add("Streaming", "Lazer", 22, 55.90m); Add("Mercado do mês", "Mercado", 30, 920.40m);
    // A internet é uma despesa mensal desde dois meses atrás; a do mês atual já está paga.
    data.Recurrences.Add(new Recurrence { Title = "Internet", Category = "Serviços", Amount = 119.90m, DayOfMonth = 8, Split = SplitMode.AvailableIncome, Participants = [ana.Id, bruno.Id], StartMonth = first.AddMonths(-2) });
    RecurrenceEngine.Generate(data, DateOnly.FromDateTime(DateTime.Today)); data.Expenses.First(e => e.RecurrenceMonth == first).Paid = true;
    // Metas: Mercado perto do limite (84%), Saúde acima (107%) e Lazer dentro (56%).
    GoalEngine.Set(data, "Mercado", 1100); GoalEngine.Set(data, "Saúde", 600); GoalEngine.Set(data, "Lazer", 100);
    // Quem pagou as contas já quitadas: o cartão de acerto mostra o saldo entre Ana e Bruno.
    data.Expenses.First(e => e.RecurrenceMonth == first).PaidBy = ana.Id; data.Expenses.First(e => e.Title == "Aluguel").PaidBy = ana.Id; data.Expenses.First(e => e.Title == "Plano de saúde").PaidBy = bruno.Id;
    return data;
}
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
            // Aciona um item do menu "⋯" da primeira linha visível (o popup em si não é simulado).
            async Task Menu(string label)
            {
                var more = main.GetVisualDescendants().OfType<Button>().First(b => AutomationProperties.GetName(b) == "Mais ações");
                ((MenuFlyout)more.Flyout!).Items.OfType<MenuItem>().First(m => AutomationProperties.GetName(m) == label).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(180);
            }
            // Uma condição falsa interrompe o roteiro e mantém o resultado como falha.
            void Check(bool ok, string description) { if (!ok) throw new Exception(description); Console.WriteLine("PASS " + description); }
            Check(main.IsVisible, "Main window opens");
            if (!string.IsNullOrEmpty(shotsDirectory))
            {
                foreach (var (theme, themeName) in new[] { (ThemePreference.Light, "claro"), (ThemePreference.Dark, "escuro") })
                {
                    App.ApplyTheme(theme);
                    foreach (var (label, file) in new[] { ("Visão geral", "visao-geral"), ("Despesas", "despesas"), ("Pessoas e rendas", "pessoas"), ("Previsões", "previsoes"), ("Ajustes", "ajustes") })
                    {
                        await Click(label, main); await Task.Delay(250);
                        using var shot = new RenderTargetBitmap(new PixelSize((int)main.Bounds.Width, (int)main.Bounds.Height), new Vector(96, 96));
                        shot.Render(main); shot.Save(Path.Combine(shotsDirectory, $"{file}-{themeName}.png"), PngBitmapEncoderOptions.Default); Console.WriteLine($"Screenshot: {file}-{themeName}");
                    }
                }
                // Diálogo de recorrentes, que mostra o estado com a internet mensal.
                await Click("Despesas", main); await Click("Recorrentes", main); await Task.Delay(250);
                var dlg = Active(); using var dshot = new RenderTargetBitmap(new PixelSize((int)dlg.Bounds.Width, (int)dlg.Bounds.Height), new Vector(96, 96));
                dshot.Render(dlg); dshot.Save(Path.Combine(shotsDirectory, "recorrentes.png"), PngBitmapEncoderOptions.Default);
                await Click("Fechar");
                // Assistente de importação de extrato, com um arquivo de demonstração (entradas, saídas e uma linha com erro).
                var demo = Path.Combine(directory, "extrato-demo.csv");
                File.WriteAllText(demo, "Data;Histórico;Valor\r\n02/11/2026;PIX PADARIA SAO JORGE;-12,50\r\n03/11/2026;SALARIO EMPRESA LTDA;3000,00\r\n04/11/2026;FARMACIA CENTRAL;-87,30\r\n05/11/2026;POSTO IPIRANGA;-210,00\r\n06/11/2026;AMAZON MARKETPLACE;-1.234,56\r\nontem;LINHA RUIM;-1,00\r\n", new System.Text.UTF8Encoding(true));
                _ = ((MainWindow)main).ImportStatementFromFile(demo); await Task.Delay(400);
                var wiz = Active(); using var wshot = new RenderTargetBitmap(new PixelSize((int)wiz.Bounds.Width, (int)wiz.Bounds.Height), new Vector(96, 96));
                wshot.Render(wiz); wshot.Save(Path.Combine(shotsDirectory, "importar-extrato.png"), PngBitmapEncoderOptions.Default);
                await Click("Cancelar");
                exitCode = 0; return;
            }
            // Cenário 1: cadastro de pessoas e rendas pela interface, incluindo o fechamento do modal ao salvar.
            await Click("Cadastrar primeira pessoa", main);
            var inputs = Active().GetVisualDescendants().OfType<TextBox>().ToArray(); inputs.First(i => i.PlaceholderText == "Ex.: Ana").Text = "Ana"; inputs.First(i => i.PlaceholderText == "Ex.: 3000,00").Text = "3000,00";
            await Click("Salvar pessoa");
            Check(lifetime.Windows.Count == 1, "Person dialog saves and closes");
            await Click("Pessoas e rendas", main); await Click("Adicionar pessoa", main);
            inputs = Active().GetVisualDescendants().OfType<TextBox>().ToArray(); inputs.First(i => i.PlaceholderText == "Ex.: Ana").Text = "Bruno"; inputs.First(i => i.PlaceholderText == "Ex.: 3000,00").Text = "1000,00"; await Click("Salvar pessoa");
            // Cenário 1b: uma pessoa pode ter várias rendas no mês; a linha "Extra" de zero mantém os totais dos cenários seguintes.
            await Click("Editar", main); await Click("Adicionar renda");
            inputs = Active().GetVisualDescendants().OfType<TextBox>().ToArray(); inputs.Where(i => i.PlaceholderText == "Ex.: Salário").Last().Text = "Extra"; inputs.Where(i => i.PlaceholderText == "Ex.: 3000,00").Last().Text = "0,00";
            await Click("Salvar pessoa");
            var anaLines = LocalStore.Read(Path.Combine(directory, "financas.json")); Check(anaLines.Incomes.Count(i => i.PersonId == anaLines.People[0].Id) == 2 && FinanceEngine.Income(anaLines, anaLines.People[0].Id, Dates.Month(DateOnly.FromDateTime(DateTime.Today))) == 3000m, "Person can have several income lines");
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
            // Pela visão geral, o botão "Marcar como pago" dos próximos pagamentos.
            // Marcar como pago pergunta quem pagou: aqui, Ana.
            await Click("Visão geral", main); await Click("Marcar como pago", main); await Click("Ana");
            var paidData = LocalStore.Read(Path.Combine(directory, "financas.json"));
            Check(paidData.Expenses.Single().Paid && paidData.Expenses.Single().PaidBy == paidData.People[0].Id, "Payment action persists with who paid");
            // Ana pagou 800 (600/200): o cartão de acerto mostra que Bruno deve 200 a Ana.
            var debtText = $"Bruno deve {Money.Format(200m)} a Ana";
            Check(main.GetVisualDescendants().OfType<TextBlock>().Any(x => AutomationProperties.GetName(x) == debtText), "Settlement card shows who owes whom");
            await Click("Registrar acerto", main); await Click("Salvar acerto");
            var settled = LocalStore.Read(Path.Combine(directory, "financas.json"));
            Check(settled.Settlements.Count == 1 && settled.Settlements[0].Amount == 200m && SettlementEngine.Debts(settled).Count == 0, "Registering a settlement clears the debt");
            Check(main.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.StartsWith("Tudo certo") == true), "Settlement card shows all settled");
            // Cenário 4: valor inválido mantém o formulário aberto e preserva o valor anteriormente salvo.
            // A edição fica no menu "⋯" da linha: aciona o item "Editar" sem depender de abrir o popup.
            await Click("Despesas", main); await Menu("Editar");
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
            Check(main.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("Estimativa para") == true), "Forecast screen renders");
            await Click("Ajustes", main);
            Check(main.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Seus dados"), "Settings screen renders");
            // Cenário 6b: categorias próprias são criadas em Ajustes e ficam disponíveis no editor de despesas.
            Active().GetVisualDescendants().OfType<TextBox>().First(i => i.PlaceholderText == "Nova categoria").Text = "Pets"; await Click("Adicionar categoria", main);
            Check(LocalStore.Read(Path.Combine(directory, "financas.json")).CustomCategories.SequenceEqual(["Pets"]), "Custom category is created in Settings");
            // Cenário 7: o tema muda na hora, vai para settings.json e não toca nas finanças.
            var financeBefore = File.ReadAllBytes(Path.Combine(directory, "financas.json"));
            await Click("Tema escuro", main);
            Check(Application.Current!.RequestedThemeVariant == ThemeVariant.Dark, "Dark theme applies immediately");
            // O fundo da janela é um degradê vindo de um DynamicResource: confirma que ele realmente mudou de cor com o tema.
            var darkBg = ((IGradientBrush)main.Background!).GradientStops[0].Color;
            await Click("Tema claro", main);
            var lightBg = ((IGradientBrush)main.Background!).GradientStops[0].Color;
            Check(Application.Current.RequestedThemeVariant == ThemeVariant.Light && darkBg != lightBg && lightBg.R > darkBg.R, "Window background follows the theme");
            Check(SettingsStore.Load(directory).Theme == ThemePreference.Light, "Theme preference persists in settings.json");
            Check(financeBefore.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "financas.json"))), "Theme change leaves financas.json untouched");
            // Cenário 8: Ctrl+N abre o editor de nova despesa de qualquer tela.
            main.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.N, KeyModifiers = Avalonia.Input.KeyModifiers.Control }); await Task.Delay(300);
            Check(lifetime.Windows.Count == 2, "Ctrl+N opens the new expense editor");
            await Click("Cancelar");
            // Cenário 9: excluir pelo menu "⋯" pede confirmação; confirmar remove só aquele lançamento.
            await Click("Despesas", main); var countBefore = LocalStore.Read(Path.Combine(directory, "financas.json")).Expenses.Count;
            await Menu("Excluir"); await Click("Confirmar");
            Check(LocalStore.Read(Path.Combine(directory, "financas.json")).Expenses.Count == countBefore - 1, "Delete from row menu removes one expense");
            // Cenário 10: despesa que se repete todo mês cria o modelo e os lançamentos até 12 meses à frente; depois é encerrada.
            await Click("Nova despesa", main);
            Active().GetVisualDescendants().OfType<TextBox>().First(i => i.PlaceholderText == "Ex.: Compra do mercado").Text = "Aluguel";
            Active().GetVisualDescendants().OfType<TextBox>().First(i => i.PlaceholderText == "Ex.: 1000,00").Text = "1500,00";
            Active().GetVisualDescendants().OfType<CheckBox>().First(c => AutomationProperties.GetName(c) == "Repetir todo mês").IsChecked = true; await Task.Delay(120);
            await Click("Salvar despesa");
            var saved = LocalStore.Read(Path.Combine(directory, "financas.json"));
            Check(saved.Recurrences.Count == 1 && saved.Expenses.Count(e => e.RecurrenceId is not null) == RecurrenceEngine.HorizonMonths + 1, "Repeat monthly creates the model and 13 months");
            await Click("Recorrentes", main); await Click("Encerrar"); await Click("Confirmar");
            saved = LocalStore.Read(Path.Combine(directory, "financas.json"));
            Check(saved.Recurrences[0].EndMonth == Dates.Month(DateOnly.FromDateTime(DateTime.Today)) && saved.Expenses.Count(e => e.RecurrenceId is not null) == 1, "Ending a recurrence keeps only the selected month");
            await Click("Fechar");
            // Cenário 10b: a busca vai a todos os meses; um termo que não existe no mês aparece ao ampliar o escopo.
            await Click("Despesas", main); await Click("Todos os meses", main);
            Check(main.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.StartsWith("Busca em todos os meses") == true), "All-months search mode opens");
            await Click("Este mês", main);
            // Cenário 10c: importar um extrato em CSV (o assistente é aberto direto no arquivo, sem o seletor nativo).
            var statement = Path.Combine(directory, "extrato.csv");
            File.WriteAllText(statement, "Data;Histórico;Valor\r\n02/11/2026;PIX PADARIA;-12,50\r\n03/11/2026;SALARIO;3000,00\r\n04/11/2026;FARMACIA CENTRAL;-1.234,56\r\n", new System.Text.UTF8Encoding(true));
            var before = LocalStore.Read(Path.Combine(directory, "financas.json")).Expenses.Count;
            _ = ((MainWindow)main).ImportStatementFromFile(statement); await Task.Delay(500);
            Check(Active().GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.StartsWith("2 despesas serão importadas") == true), "Import wizard previews the statement");
            await Click("Importar");
            var imported = LocalStore.Read(Path.Combine(directory, "financas.json"));
            Check(imported.Expenses.Count == before + 2 && imported.Expenses.Any(e => e.Title == "FARMACIA CENTRAL" && e.Amount == 1234.56m && e.Paid && e.PaidBy is null), "Statement import creates paid expenses");
            // Importar o mesmo arquivo outra vez não repete nada: o botão Importar fica desligado e o aviso mostra as repetidas.
            _ = ((MainWindow)main).ImportStatementFromFile(statement); await Task.Delay(500);
            Check(Active().GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.Contains("2 linha(s) repetida(s)") == true) && !FindButton(Active(), "Importar").IsEnabled, "Re-importing the same statement finds only duplicates");
            await Click("Cancelar");
            // Cenário 11: meta de Mercado (R$ 500) abaixo do gasto (R$ 800): salva e a barra aparece em alerta vermelho.
            await Click("Visão geral", main); await Click("Metas", main);
            Active().GetVisualDescendants().OfType<TextBox>().First(i => AutomationProperties.GetName(i) == "Meta de Mercado").Text = "500,00"; await Click("Salvar metas");
            var goalSaved = LocalStore.Read(Path.Combine(directory, "financas.json"));
            Check(goalSaved.Goals.Count == 1 && goalSaved.Goals[0].Category == "Mercado" && goalSaved.Goals[0].Amount == 500m, "Goal is saved from the dialog");
            Check(main.GetVisualDescendants().OfType<ProgressBar>().Any(b => b.Classes.Contains("over")), "Goal over the limit is highlighted");
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
