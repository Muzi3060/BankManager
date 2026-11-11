using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Quem pagou e acerto de contas: a pergunta "Quem pagou?", o cartão da visão geral, o registro de acertos e o histórico.
public sealed partial class MainWindow
{
    /// <summary>
    /// Nome de uma pessoa pelo identificador; um registro órfão (não deveria existir) aparece como "—".
    /// </summary>
    private string NameOf(Guid id) => data.People.FirstOrDefault(p => p.Id == id)?.Name ?? "—";
    /// <summary>
    /// Pergunta quem pagou uma despesa. Com uma só pessoa cadastrada não pergunta. Em despesa individual, o responsável vem destacado.
    /// </summary>
    /// <returns>Se o usuário escolheu (falso ao cancelar) e a pessoa (nula em "Não informar").</returns>
    private async Task<(bool Chosen, Guid? Payer)> AskPayer(Expense expense)
    {
        if (data.People.Count == 1) return (true, data.People[0].Id);
        var result = (Chosen: false, Payer: (Guid?)null);
        Window? dialog = null;
        var suggested = expense.Split == SplitMode.Individual && expense.Participants.Count == 1 ? expense.Participants[0] : (Guid?)null;
        var panel = Stack(Heading("Quem pagou?", $"{expense.Title} · {Money.Format(expense.Amount)}"));
        foreach (var person in data.People)
        {
            var id = person.Id; var button = Button(person.Name, () => { result = (true, id); dialog!.Close(); }, id == suggested ? "primary" : null); button.HorizontalAlignment = HorizontalAlignment.Stretch; panel.Children.Add(button);
        }
        var none = Button("Não informar", () => { result = (true, null); dialog!.Close(); }, "ghost"); none.HorizontalAlignment = HorizontalAlignment.Stretch; panel.Children.Add(none);
        panel.Children.Add(Text("Quem pagou entra no acerto de contas: quem não pagou deve a sua parte a quem pagou. Sem informar, a despesa fica fora do acerto.", "muted"));
        panel.Children.Add(Button("Cancelar", () => dialog!.Close(), "ghost"));
        dialog = Dialog("Quem pagou?", panel, 460); dialog.Height = Math.Min(680, 330 + 50 * data.People.Count);
        await dialog.ShowDialog(this);
        return result;
    }
    /// <summary>
    /// Cartão "Acerto de contas": quem deve a quem (saldo acumulado de todos os meses), com ação para registrar um acerto.
    /// </summary>
    private Control SettlementCard()
    {
        var debts = SettlementEngine.Debts(data);
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        heading.Children.Add(Text("Acerto de contas", "section"));
        if (data.Settlements.Count > 0) { var history = Button("Histórico", () => _ = Run(ShowSettlements), "link"); Grid.SetColumn(history, 1); heading.Children.Add(history); }
        var panel = Stack(heading); panel.Spacing = 0;
        if (debts.Count == 0)
        {
            var anyPayer = data.Settlements.Count > 0 || data.Expenses.Any(e => e.Paid && e.PaidBy is not null);
            panel.Children.Add(Text(anyPayer ? "Tudo certo: ninguém deve nada a ninguém." : "Ao marcar uma despesa como paga, informe quem pagou. O BankManager mostra aqui quem deve a quem.", "muted"));
        }
        foreach (var debt in debts)
        {
            panel.Children.Add(Divider());
            var (debtor, creditor) = (NameOf(debt.DebtorId), NameOf(debt.CreditorId));
            // A frase em negrito nos nomes; o nome de automação traz o texto completo (leitores de tela e testes).
            var sentence = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            sentence.Inlines!.Add(new Run(debtor) { FontWeight = FontWeight.SemiBold }); sentence.Inlines.Add(new Run(" deve a ")); sentence.Inlines.Add(new Run(creditor) { FontWeight = FontWeight.SemiBold });
            AutomationProperties.SetName(sentence, $"{debtor} deve {Money.Format(debt.Amount)} a {creditor}");
            var amount = Text(Money.Format(debt.Amount), "num"); amount.FontWeight = FontWeight.SemiBold; amount.VerticalAlignment = VerticalAlignment.Center; amount.TextAlignment = TextAlignment.Right; amount.MinWidth = 110; amount.Margin = new Thickness(16, 0);
            var settle = Button("Registrar acerto", () => _ = Run(() => RegisterSettlement(debt)));
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 10) };
            Grid.SetColumn(amount, 1); Grid.SetColumn(settle, 2); row.Children.Add(sentence); row.Children.Add(amount); row.Children.Add(settle); panel.Children.Add(row);
        }
        if (debts.Count > 0) panel.Children.Add(Text("Considera as despesas pagas com “quem pagou” informado e os acertos já registrados, de todos os meses.", "muted"));
        return Card(panel);
    }
    /// <summary>
    /// Registra um pagamento entre duas pessoas. Parte de uma dívida (quem deve paga a quem recebe, no valor devido, editável para pagamentos parciais).
    /// </summary>
    private async Task RegisterSettlement(Debt? suggestion = null)
    {
        var names = data.People.Select(p => p.Name).ToList();
        var from = Combo(names, suggestion is null ? 0 : data.People.FindIndex(p => p.Id == suggestion.DebtorId));
        var to = Combo(names, suggestion is null ? Math.Min(1, names.Count - 1) : data.People.FindIndex(p => p.Id == suggestion.CreditorId));
        var amount = Input(suggestion?.Amount.ToString("0.00", Money.Culture), "Ex.: 200,00", 12);
        var date = Input(DateTime.Today.ToString("dd/MM/yyyy"), "dd/mm/aaaa", 10);
        var error = Text("", "error");
        var panel = Stack(Heading("Registrar acerto", "Um pagamento entre duas pessoas para quitar o que uma deve à outra."),
            Columns(Field("Quem pagou", from), Field("Quem recebeu", to)), Columns(Field("Valor (R$)", amount, "Pode ser menor que o devido."), Field("Data (dd/mm/aaaa)", date)), error);
        var dialog = Dialog("Registrar acerto", panel, 600); dialog.Height = 480;
        panel.Children.Add(Row(Button("Cancelar", () => dialog.Close()), Button("Salvar acerto", () =>
        {
            try
            {
                if (from.SelectedIndex == to.SelectedIndex) throw new BudgetException("Escolha duas pessoas diferentes.");
                if (!DateOnly.TryParseExact(date.Text, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) throw new BudgetException("Data: informe uma data válida no formato dd/mm/aaaa.");
                var settlement = new Settlement { PayerId = data.People[from.SelectedIndex].Id, ReceiverId = data.People[to.SelectedIndex].Id, Amount = Money.Parse(amount.Text, "Valor", true), Date = day };
                Commit(d => d.Settlements.Add(settlement)); dialog.Close();
            }
            catch (Exception ex) { error.Text = FriendlyError(ex); }
        }, "primary")));
        await dialog.ShowDialog(this);
    }
    /// <summary>
    /// Lista os acertos registrados, do mais recente ao mais antigo, e permite excluir um registrado por engano (a dívida volta).
    /// </summary>
    private async Task ShowSettlements()
    {
        var holder = new ContentControl(); Window? dialog = null;
        Control Build()
        {
            var rows = new StackPanel();
            foreach (var s in data.Settlements.OrderByDescending(x => x.Date))
            {
                if (rows.Children.Count > 0) rows.Children.Add(Divider());
                var id = s.Id;
                var text = Text($"{s.Date:dd/MM/yyyy} · {NameOf(s.PayerId)} pagou a {NameOf(s.ReceiverId)}"); text.VerticalAlignment = VerticalAlignment.Center;
                var amount = Text(Money.Format(s.Amount), "num"); amount.FontWeight = FontWeight.SemiBold; amount.VerticalAlignment = VerticalAlignment.Center; amount.Margin = new Thickness(16, 0);
                var delete = Button("Excluir acerto", () => _ = Run(async () =>
                {
                    if (await Confirm(dialog!, "Excluir acerto?", "O acerto sai do histórico e a dívida correspondente volta a aparecer.")) Commit(d => d.Settlements.RemoveAll(x => x.Id == id));
                    holder.Content = Build();
                }), "ghost danger", "Trash", iconOnly: true);
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 8) };
                Grid.SetColumn(amount, 1); Grid.SetColumn(delete, 2); row.Children.Add(text); row.Children.Add(amount); row.Children.Add(delete); rows.Children.Add(row);
            }
            if (data.Settlements.Count == 0) rows.Children.Add(Text("Nenhum acerto registrado.", "muted"));
            return rows;
        }
        holder.Content = Build();
        var panel = Stack(Heading("Histórico de acertos", "Pagamentos já feitos entre as pessoas."), holder);
        dialog = Dialog("Histórico de acertos", panel, 640); dialog.Height = 520;
        panel.Children.Add(Row(Button("Fechar", () => dialog.Close(), "primary")));
        await dialog.ShowDialog(this);
    }
}
