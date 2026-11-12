using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Compartilhar despesas, parte de quem envia: o diálogo que mostra o que vai no arquivo e o salva. A escolha das despesas é feita
// antes, no modo de seleção da página Despesas (veja ExpensesPage.cs).
public sealed partial class MainWindow
{
    private static readonly FilePickerFileType ShareFiles = new("Despesas compartilhadas do BankManager") { Patterns = ["*" + ShareFile.Extension] };
    // Versão curta do aplicativo ("2.2"), gravada no arquivo para informar quem recebe.
    private static string AppVersionLabel => typeof(MainWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}" : "";
    /// <summary>
    /// Diálogo "Compartilhar despesas": parte das despesas marcadas na lista e mostra quantas vão, o total e as pessoas citadas.
    /// Duas opções, desligadas por padrão: incluir as outras parcelas de uma compra parcelada e incluir a situação de pagamento.
    /// "Salvar arquivo" abre o seletor do sistema; depois de salvar, o modo de seleção termina.
    /// </summary>
    private void OpenShareExport()
    {
        var chosen = data.Expenses.Where(e => selectedIds.Contains(e.Id)).ToList();
        if (chosen.Count == 0 || OverlayOpen) return;
        var names = data.People.ToDictionary(p => p.Id, p => p.Name);
        // Para cada compra parcelada com alguma parcela marcada: as parcelas que ficaram de fora.
        var groups = chosen.Where(e => e.InstallmentGroup is not null).GroupBy(e => e.InstallmentGroup!.Value)
            .Select(g => (Title: g.First().Title, Marked: g.Count(), Others: data.Expenses.Where(e => e.InstallmentGroup == g.Key && !selectedIds.Contains(e.Id)).ToList())).Where(g => g.Others.Count > 0).ToList();
        // Caixa de marcação com uma linha de explicação embaixo, que muda conforme ligada ou desligada.
        (CheckBox Check, Control View) Option(string label, string off, string on, Action changed)
        {
            var check = new CheckBox { Content = new TextBlock { Text = label, FontWeight = FontWeight.SemiBold }, IsChecked = false }; AutomationProperties.SetName(check, label);
            var hint = Text("Desligado: " + off, "muted"); hint.Classes.Add("small"); hint.Margin = new Thickness(28, 0, 0, 0);
            check.IsCheckedChanged += (_, _) => { hint.Text = check.IsChecked == true ? "Ligado: " + on : "Desligado: " + off; changed(); };
            var view = Stack(check, hint); view.Spacing = 0; return (check, view);
        }
        var count = Text("", "kpi"); var total = Text("", "kpi"); var people = Text("", "kpi"); people.TextTrimming = TextTrimming.CharacterEllipsis; people.TextWrapping = TextWrapping.NoWrap;
        var list = new StackPanel(); var error = Text("", "error"); error.IsVisible = false;
        var groupOptions = new List<(CheckBox Check, List<Expense> Others)>(); CheckBox? payment = null;
        // Despesas que vão no arquivo: as marcadas, mais as outras parcelas das compras cuja opção foi ligada.
        List<Expense> Effective() => chosen.Concat(groupOptions.Where(g => g.Check.IsChecked == true).SelectMany(g => g.Others)).OrderBy(e => e.DueDate).ThenBy(e => e.Title).ToList();
        // Indicadores e lista acompanham as opções.
        void Update()
        {
            var items = Effective(); var withPayment = payment?.IsChecked == true;
            count.Text = items.Count.ToString(); total.Text = Money.Format(items.Sum(e => e.Amount));
            people.Text = string.Join(", ", items.SelectMany(e => withPayment && e.PaidBy is { } p ? e.Participants.Append(p) : e.Participants).Distinct().Select(id => names[id])); ToolTip.SetTip(people, people.Text);
            list.Children.Clear();
            foreach (var e in items)
            {
                if (list.Children.Count > 0) list.Children.Add(Divider());
                var category = e.Category + (data.CustomCategories.Contains(e.Category) ? " (categoria sua)" : "");
                var detail = category + (e.InstallmentCount > 1 ? $" · parcela {e.InstallmentNumber} de {e.InstallmentCount}" : e.RecurrenceId is not null ? " · recorrente" : "") + $" · {e.DueDate:dd/MM}" + (e.DueDate.Year != month.Year ? $"/{e.DueDate:yy}" : "");
                var title = Stack(new TextBlock { Text = e.Title, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis }, Text(detail, "muted").WithClass("small")); title.Spacing = 1;
                var amount = Text(Money.Format(e.Amount), "strong"); amount.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(amount, 1);
                list.Children.Add(new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 8), Children = { title, amount } });
            }
        }
        // Faixa de três indicadores, separados por linhas finas.
        Control Stat(string label, TextBlock value, int column) { value.FontSize = 22; var stat = Stack(Text(label, "muted").WithClass("small"), value); stat.Spacing = 2; stat.Margin = new Thickness(column == 0 ? 0 : 18, 0, 18, 0); Grid.SetColumn(stat, column * 2); return stat; }
        var stats = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*,Auto,1.3*") };
        foreach (var (label, value, column) in new[] { ("Despesas", count, 0), ("Total", total, 1), ("Pessoas citadas", people, 2) }) stats.Children.Add(Stat(label, value, column));
        foreach (var column in new[] { 1, 3 }) { var line = new Border { Width = 1, Height = double.NaN }.WithClass("divider"); Grid.SetColumn(line, column); stats.Children.Add(line); }
        var body = Stack(new Border { Child = stats }.WithClass("panel"), new ScrollViewer { Content = list, MaxHeight = 210 }); body.Spacing = 14;
        foreach (var group in groups)
        {
            var n = group.Others.Count; var which = group.Marked == 1 ? "vai só a parcela marcada." : $"vão só as {group.Marked} parcelas marcadas.";
            var option = Option($"Incluir {(n == 1 ? "a outra parcela" : $"as outras {n} parcelas")} de {group.Title}", which, $"vão todas as parcelas de {group.Title}.", Update);
            groupOptions.Add((option.Check, group.Others)); body.Children.Add(option.View);
        }
        var paymentOption = Option("Incluir situação de pagamento e quem pagou", "a outra pessoa recebe tudo como “em aberto”.", "vão também pago/em aberto, a data do pagamento e quem pagou.", Update);
        payment = paymentOption.Check; body.Children.Add(paymentOption.View);
        var notGoing = new TextBlock(); notGoing.Inlines!.Add(new Avalonia.Controls.Documents.Run("Não vai no arquivo: ") { FontWeight = FontWeight.SemiBold }); notGoing.Inlines.Add(new Avalonia.Controls.Documents.Run("rendas, metas, acertos de contas e as outras despesas."));
        body.Children.Add(Banner("Lock", notGoing)); body.Children.Add(Banner("Alert", "O arquivo é texto legível, sem senha. Envie só para quem deve receber.", "warn")); body.Children.Add(error);
        // Nome sugerido: despesas-<mês abreviado>-<ano>.bmshare.json, com o mês selecionado na janela.
        var suggested = $"despesas-{month.ToString("MMM", Money.Culture).TrimEnd('.').ToLower(Money.Culture)}-{month.Year}{ShareFile.Extension}";
        var fileName = Stack(Text("Nome do arquivo", "muted").WithClass("small"), Text(suggested, "mono")); fileName.Spacing = 2;
        var save = Button("Salvar arquivo", () => { }, "primary", "Download");
        save.Click += async (_, _) =>
        {
            try
            {
                error.IsVisible = false;
                var file = ExpenseShare.Build(data, Effective().Select(e => e.Id).ToList(), payment.IsChecked == true, AppVersionLabel);
                var path = TestSavePath;
                if (path is null)
                {
                    var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Salvar despesas compartilhadas", SuggestedFileName = suggested, FileTypeChoices = [ShareFiles] });
                    if (target is null) return;
                    path = target.TryGetLocalPath() ?? throw new BudgetException("Escolha uma pasta local.");
                    // O seletor pode devolver o nome sem a extensão própria; ela identifica o tipo do arquivo.
                    if (!path.EndsWith(ShareFile.Extension, StringComparison.OrdinalIgnoreCase)) path += ShareFile.Extension;
                }
                RejectInternalPath(path); ExpenseShare.Save(path, file);
                // Confirmação com o local do arquivo; a seleção termina porque o trabalho dela acabou.
                CloseOverlay(); StopSelecting();
                Notify($"{file.Expenses.Count} {(file.Expenses.Count == 1 ? "despesa salva" : "despesas salvas")} em {path}. Envie o arquivo só para quem deve recebê-lo.", success: true);
            }
            catch (Exception ex) { error.Text = FriendlyError(ex); error.IsVisible = true; }
        };
        // Cancelar, o "X" e a tecla Esc fecham o diálogo e mantêm a seleção, para a pessoa poder ajustá-la.
        var (card, heading) = ModalCard("Compartilhar despesas", "Gera um arquivo que outra pessoa pode importar sem perder os dados dela.", body, ModalFooter(fileName, Button("Cancelar", CloseOverlay), save), CloseOverlay);
        Update(); ClearNotice(); ShowOverlay(card, CloseOverlay, heading);
    }
}
