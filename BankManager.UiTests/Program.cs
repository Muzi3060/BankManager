using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BankManager;
using BankManager.Core;

var directory = Path.Combine(Path.GetTempPath(), "bankmanager-ui-" + Guid.NewGuid());
Environment.SetEnvironmentVariable("BANKMANAGER_DATA_DIR", directory);
int exitCode = 1;
AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().AfterSetup(builder =>
{
    var lifetime = (IClassicDesktopStyleApplicationLifetime)builder.Instance!.ApplicationLifetime!;
    lifetime.Startup += (_, _) => DispatcherTimer.RunOnce(async () =>
    {
        try
        {
            var main = lifetime.MainWindow!;
            Window Active() => lifetime.Windows.Last();
            Button FindButton(Window window, string label) => window.GetVisualDescendants().OfType<Button>().First(b => b.Content is string s && s == label);
            async Task Click(string label, Window? window = null)
            {
                FindButton(window ?? Active(), label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(180);
            }
            void Check(bool ok, string description) { if (!ok) throw new Exception(description); Console.WriteLine("PASS " + description); }
            Check(main.IsVisible, "Main window opens");
            await Click("Cadastrar primeira pessoa", main);
            var inputs = Active().GetVisualDescendants().OfType<TextBox>().ToArray(); inputs[0].Text = "Ana"; inputs[1].Text = "3000,00";
            await Click("Salvar pessoa");
            Check(lifetime.Windows.Count == 1, "Person dialog saves and closes");
            await Click("♙   Pessoas e rendas", main); await Click("+  Adicionar pessoa", main);
            inputs = Active().GetVisualDescendants().OfType<TextBox>().ToArray(); inputs[0].Text = "Bruno"; inputs[1].Text = "1000,00"; await Click("Salvar pessoa");
            await Click("☷   Despesas", main); await Click("+  Nova despesa", main);
            inputs = Active().GetVisualDescendants().OfType<TextBox>().ToArray();
            inputs.First(i => i.PlaceholderText == "Ex.: Compra do mercado").Text = "Mercado da família";
            inputs.First(i => i.PlaceholderText == "Ex.: 1000,00").Text = "800,00";
            await Click("Conferir divisão antes de salvar");
            Check(Active().GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("600,00") == true && t.Text.Contains("200,00")), "Proportional preview renders 600 / 200");
            await Click("Salvar despesa");
            Check(LocalStore.Read(Path.Combine(directory, "financas.json")).Expenses.Single().Amount == 800m, "Expense persists from UI");
            await Click("Marcar como pago", main);
            Check(LocalStore.Read(Path.Combine(directory, "financas.json")).Expenses.Single().Paid, "Payment action persists");
            await Click("Detalhes / editar", main);
            inputs = Active().GetVisualDescendants().OfType<TextBox>().ToArray(); inputs.First(i => i.PlaceholderText == "Ex.: 1000,00").Text = "-1";
            await Click("Salvar despesa");
            Check(lifetime.Windows.Count == 2, "Invalid expense keeps editor open");
            Check(LocalStore.Read(Path.Combine(directory, "financas.json")).Expenses.Single().Amount == 800m, "Invalid edit preserves saved value");
            await Click("Cancelar");
            await Click("+  Nova despesa", main);
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
            await Click("↗   Previsões", main);
            Check(main.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Olhe para o próximo mês"), "Forecast screen renders");
            await Click("⚙   Dados e ajuda", main);
            Check(main.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Backup e recuperação"), "Backup screen renders");
            await Click("⌂   Visão geral", main);
            var output = Environment.GetEnvironmentVariable("BANKMANAGER_SCREENSHOT");
            if (!string.IsNullOrEmpty(output))
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)main.Bounds.Width, (int)main.Bounds.Height), new Vector(96, 96));
                bitmap.Render(main); bitmap.Save(output, PngBitmapEncoderOptions.Default); Console.WriteLine("Screenshot: " + output);
            }
            exitCode = 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); }
        finally { lifetime.Shutdown(exitCode); }
    }, TimeSpan.FromSeconds(1));
}).StartWithClassicDesktopLifetime(args);
try { Directory.Delete(directory, true); } catch (IOException) { }
return exitCode;
