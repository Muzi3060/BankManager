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
    /// Pergunta quem pagou uma despesa e em que dia (padrão: hoje). Com uma só pessoa cadastrada não pergunta: ela pagou hoje, e a
    /// data pode ser corrigida ao editar a despesa. Em despesa individual, o responsável vem destacado.
    /// </summary>
    /// <returns>Se o usuário escolheu (falso ao cancelar), a pessoa (nula em "Não informar") e a data do pagamento.</returns>
    private async Task<(bool Chosen, Guid? Payer, DateOnly PaidDate)> AskPayer(Expense expense)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (data.People.Count == 1) return (true, data.People[0].Id, today);
        var result = (Chosen: false, Payer: (Guid?)null, PaidDate: today);
        Window? dialog = null; var error = Text("", "error");
        var date = Input(today.ToString("dd/MM/yyyy"), "dd/mm/aaaa", 10);
        // Só fecha com uma data válida; caso contrário, mostra o erro e deixa corrigir.
        void Choose(Guid? payer)
        {
            try { result = (true, payer, ParseDate(date.Text, "Data do pagamento")); dialog!.Close(); }
            catch (Exception ex) { error.Text = FriendlyError(ex); }
        }
        var suggested = expense.Split == SplitMode.Individual && expense.Participants.Count == 1 ? expense.Participants[0] : (Guid?)null;
        var panel = Stack(Heading("Quem pagou?", $"{expense.Title} · {Money.Format(expense.Amount)}"), Field("Data do pagamento (dd/mm/aaaa)", date), error);
        foreach (var person in data.People)
        {
            var id = person.Id; var button = Button(person.Name, () => Choose(id), id == suggested ? "primary" : null); button.HorizontalAlignment = HorizontalAlignment.Stretch; panel.Children.Add(button);
        }
        var none = Button("Não informar", () => Choose(null), "ghost"); none.HorizontalAlignment = HorizontalAlignment.Stretch; panel.Children.Add(none);
        panel.Children.Add(Text("Quem pagou entra no acerto de contas: quem não pagou deve a sua parte a quem pagou. Sem informar, a despesa fica fora do acerto.", "muted"));
        panel.Children.Add(Button("Cancelar", () => dialog!.Close(), "ghost"));
        dialog = Dialog("Quem pagou?", panel, 460); dialog.Height = Math.Min(760, 440 + 50 * data.People.Count);
        await dialog.ShowDialog(this);
        return result;
    }
    /// <summary>
    /// Cartão "Acerto de contas": quem deve a quem (saldo acumulado de todos os meses), com os avatares das duas pessoas
    /// e a ação de registrar um acerto. Sem dívidas, mostra "Tudo certo por aqui".
    /// </summary>
    /// <param name="history">Mostra o link "Histórico" (a aba de acerto já traz o histórico na própria página).</param>
    private Control SettlementCard(bool history = true)
    {
        var debts = SettlementEngine.Debts(data);
        var panel = Stack(SectionHeading("Acerto de contas", history && data.Settlements.Count > 0 ? Button("Histórico", () => _ = Run(ShowSettlements), "link") : null)); panel.Spacing = 10;
        if (debts.Count == 0)
        {
            var anyPayer = data.Settlements.Count > 0 || data.Expenses.Any(e => e.Paid && e.PaidBy is not null);
            var empty = Stack(Text("Tudo certo por aqui", "strong"), Text(anyPayer ? "Ninguém deve nada a ninguém." : "Ao marcar uma despesa como paga, informe quem pagou. O BankManager mostra aqui quem deve a quem.", "muted")); empty.Spacing = 4;
            panel.Children.Add(empty);
        }
        foreach (var debt in debts)
        {
            if (debt != debts[0]) panel.Children.Add(Divider());
            var (debtor, creditor) = (NameOf(debt.DebtorId), NameOf(debt.CreditorId));
            // Avatares com uma seta entre eles: quem deve (neutro) paga a quem recebe (em destaque).
            var arrow = Icon("ArrowRight", 18); arrow.Margin = new Thickness(12, 0);
            var avatars = new StackPanel { Orientation = Orientation.Horizontal }; avatars.Children.Add(Avatar(debtor).WithClass("large")); avatars.Children.Add(arrow); avatars.Children.Add(Avatar(creditor, strong: true).WithClass("large"));
            // Só o valor em negrito; o nome de automação traz a frase completa (leitores de tela e testes).
            var sentence = new TextBlock { TextWrapping = TextWrapping.Wrap };
            sentence.Inlines!.Add(new Run(debtor + " deve ")); sentence.Inlines.Add(new Run(Money.Format(debt.Amount)) { FontWeight = FontWeight.SemiBold }); sentence.Inlines.Add(new Run(" a " + creditor));
            AutomationProperties.SetName(sentence, $"{debtor} deve {Money.Format(debt.Amount)} a {creditor}");
            var settle = Button("Registrar acerto", () => _ = Run(() => RegisterSettlement(debt)), "ink"); settle.HorizontalAlignment = HorizontalAlignment.Left;
            var block = Stack(avatars, sentence, settle); block.Spacing = 12; settle.Margin = new Thickness(0, 6, 0, 0); panel.Children.Add(block);
        }
        if (debts.Count > 0) panel.Children.Add(Text("Saldo acumulado de todos os meses, já descontados os acertos registrados.", "muted").WithClass("small"));
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
    /// Linhas do histórico de acertos, do mais recente ao mais antigo, com a opção de excluir um registrado por engano (a dívida volta).
    /// Usadas no diálogo de histórico e na aba de acerto de "Previsões e metas".
    /// </summary>
    /// <param name="owner">Janela sobre a qual a confirmação abre.</param>
    /// <param name="after">Chamado depois de uma exclusão, para o diálogo refazer a lista (a página se refaz sozinha).</param>
    private Control SettlementRows(Func<Window> owner, Action after)
    {
        var rows = new StackPanel();
        foreach (var s in data.Settlements.OrderByDescending(x => x.Date))
        {
            if (rows.Children.Count > 0) rows.Children.Add(Divider());
            var id = s.Id;
            var text = Text($"{s.Date:dd/MM/yyyy} · {NameOf(s.PayerId)} pagou a {NameOf(s.ReceiverId)}"); text.VerticalAlignment = VerticalAlignment.Center;
            var amount = Text(Money.Format(s.Amount), "strong"); amount.VerticalAlignment = VerticalAlignment.Center; amount.Margin = new Thickness(16, 0);
            var delete = Button("Excluir acerto", () => _ = Run(async () =>
            {
                if (await Confirm(owner(), "Excluir acerto?", "O acerto sai do histórico e a dívida correspondente volta a aparecer.")) Commit(d => d.Settlements.RemoveAll(x => x.Id == id));
                after();
            }), "ghost round danger", "Trash", iconOnly: true);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 6) };
            Grid.SetColumn(amount, 1); Grid.SetColumn(delete, 2); row.Children.Add(text); row.Children.Add(amount); row.Children.Add(delete); rows.Children.Add(row);
        }
        if (data.Settlements.Count == 0) rows.Children.Add(Text("Nenhum acerto registrado ainda.", "muted"));
        return rows;
    }
    /// <summary>
    /// Abre o histórico de acertos em um diálogo (atalho a partir do cartão da visão geral).
    /// </summary>
    private async Task ShowSettlements()
    {
        var holder = new ContentControl(); Window? dialog = null;
        void Refresh() => holder.Content = SettlementRows(() => dialog!, Refresh);
        Refresh();
        var panel = Stack(Heading("Histórico de acertos", "Pagamentos já feitos entre as pessoas."), holder);
        dialog = Dialog("Histórico de acertos", panel, 640); dialog.Height = 520;
        panel.Children.Add(Row(Button("Fechar", () => dialog.Close(), "primary")));
        await dialog.ShowDialog(this);
    }
}
