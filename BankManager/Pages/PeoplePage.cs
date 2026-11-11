using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Página "Pessoas e rendas": cadastro de quem divide as despesas e da renda de cada mês.
public sealed partial class MainWindow
{
    /// <summary>
    /// Lista pessoas e a renda da competência em linhas, com edição e exclusão protegida por referências no histórico.
    /// </summary>
    private Control PeoplePage()
    {
        var actions = Row(Button("Adicionar pessoa", () => _ = Run(() => EditPerson()), "primary", "Plus"), Button("Copiar rendas do mês anterior", () => _ = Run(CopyIncome)));
        var panel = Stack(PageHeader("Pessoas e rendas", "Quem divide as despesas e quanto cada pessoa recebe neste mês."), actions); panel.Spacing = 20;
        if (data.People.Count == 0)
        {
            var empty = Stack(Text("Ninguém por aqui ainda", "section"), Text("Adicione você e as pessoas que compartilham as despesas. Depois informe a renda de cada uma neste mês.", "muted")); empty.Spacing = 8; empty.Margin = new Thickness(24, 40);
            empty.HorizontalAlignment = HorizontalAlignment.Center; foreach (var t in empty.Children.OfType<TextBlock>()) t.TextAlignment = TextAlignment.Center;
            panel.Children.Add(Card(empty)); return panel;
        }
        var rows = new StackPanel();
        foreach (var person in data.People)
        {
            if (rows.Children.Count > 0) rows.Children.Add(Divider());
            // Resumo das rendas do mês: nenhuma (alerta), uma (só o nome) ou várias ("Salário R$ 3.000,00 · Extra R$ 500,00").
            var lines = FinanceEngine.IncomeLines(data, person.Id, month);
            var status = lines.Count == 0 ? "Renda ainda não informada neste mês" : lines.Count == 1 ? lines[0].Label : string.Join(" · ", lines.Select(l => $"{l.Label} {Money.Format(l.Amount)}"));
            var who = Stack(new TextBlock { Text = person.Name, FontWeight = FontWeight.Medium }, Text(status, lines.Count == 0 ? "warn" : "muted")); who.Spacing = 0; who.VerticalAlignment = VerticalAlignment.Center;
            var avatar = Avatar(person.Name); avatar.Margin = new Thickness(0, 0, 12, 0);
            var income = Text(Money.Format(FinanceEngine.Income(data, person.Id, month)), "num"); income.FontWeight = FontWeight.SemiBold; income.VerticalAlignment = VerticalAlignment.Center; income.Margin = new Thickness(16, 0);
            var edit = Button("Editar", () => _ = Run(() => EditPerson(person)));
            var delete = Button("Excluir pessoa", () => _ = Run(async () =>
            {
                // Não permite apagar referências utilizadas em nenhum mês, evitando despesas sem responsável.
                if (data.Expenses.Any(e => e.Participants.Contains(person.Id))) throw new BudgetException("Esta pessoa possui despesas no histórico. Remova ou reatribua essas despesas antes de excluí-la.");
                if (data.Expenses.Any(e => e.PaidBy == person.Id) || data.Settlements.Any(s => s.PayerId == person.Id || s.ReceiverId == person.Id)) throw new BudgetException("Esta pessoa aparece como quem pagou uma despesa ou em um acerto de contas. Remova esses registros antes de excluí-la.");
                if (data.Recurrences.Any(r => r.Participants.Contains(person.Id))) throw new BudgetException("Esta pessoa participa de uma despesa recorrente. Em Despesas → Recorrentes, encerre e remova a recorrência antes de excluí-la.");
                if (await Confirm(this, "Excluir pessoa?", $"Excluir {person.Name} e todos os seus registros de renda?")) Commit(d => { d.People.RemoveAll(p => p.Id == person.Id); d.Incomes.RemoveAll(i => i.PersonId == person.Id); });
            }), "ghost danger", "Trash", iconOnly: true);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center }; buttons.Children.Add(edit); buttons.Children.Add(delete);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(20, 12) };
            Grid.SetColumn(who, 1); Grid.SetColumn(income, 2); Grid.SetColumn(buttons, 3); row.Children.Add(avatar); row.Children.Add(who); row.Children.Add(income); row.Children.Add(buttons);
            rows.Children.Add(row);
        }
        var card = Card(rows); card.Padding = new Thickness(0); panel.Children.Add(card);
        panel.Children.Add(Text("Cada mês guarda sua própria renda. Alterar o nome vale para todo o histórico; alterar a renda afeta apenas este mês e recalcula a divisão dele.", "muted"));
        return panel;
    }
    /// <summary>
    /// Copia rendas da competência anterior apenas para pessoas sem registro no mês selecionado, após confirmação.
    /// </summary>
    private async Task CopyIncome()
    {
        var previous = data.Incomes.Where(i => i.Month == month.AddMonths(-1) && !data.Incomes.Any(current => current.Month == month && current.PersonId == i.PersonId)).ToList();
        if (previous.Count == 0) throw new BudgetException("Não há rendas anteriores para copiar. Rendas já informadas neste mês não são substituídas.");
        if (await Confirm(this, "Copiar rendas?", $"Preencher {previous.Count} renda(s) ainda não informada(s) com os valores do mês anterior?")) Commit(d => d.Incomes.AddRange(previous.Select(i => i with { Month = month, Id = Guid.NewGuid() })));
    }
    /// <summary>
    /// Abre o cadastro ou edição de pessoa. O nome é global; as rendas (uma ou mais linhas, como salário e extra) pertencem ao mês selecionado.
    /// </summary>
    private async Task EditPerson(Person? person = null)
    {
        var name = Input(person?.Name, "Ex.: Ana", 80);
        // Linhas de renda em edição, com o Id das existentes preservado. Uma pessoa nova já começa com a linha "Salário".
        var lines = new List<(Guid Id, TextBox Label, TextBox Amount)>();
        if (person is not null) foreach (var income in FinanceEngine.IncomeLines(data, person.Id, month)) lines.Add((income.Id, Input(income.Label, "Ex.: Salário", 40), Input(income.Amount.ToString("0.00", Money.Culture), "Ex.: 3000,00", 12)));
        else lines.Add((Guid.NewGuid(), Input("Salário", "Ex.: Salário", 40), Input("", "Ex.: 3000,00", 12)));
        var linePanel = new StackPanel { Spacing = 8 };
        // Reconstrói a lista de linhas depois de adicionar ou remover uma.
        void Refresh()
        {
            // Solta os campos das linhas antigas: um controle só pode ter um pai.
            foreach (var old in linePanel.Children.OfType<Grid>()) old.Children.Clear();
            linePanel.Children.Clear();
            if (lines.Count == 0) linePanel.Children.Add(Text("Nenhuma renda neste mês: ela ficará como “não informada”.", "muted"));
            foreach (var line in lines.ToList())
            {
                AutomationProperties.SetName(line.Label, "Nome da renda"); AutomationProperties.SetName(line.Amount, "Valor da renda (R$)");
                var remove = Button("Remover renda", () => { lines.Remove(line); Refresh(); }, "ghost danger", "Trash", iconOnly: true);
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,170,Auto") }; line.Label.Margin = new Thickness(0, 0, 8, 0); line.Amount.Margin = new Thickness(0, 0, 8, 0);
                Grid.SetColumn(line.Amount, 1); Grid.SetColumn(remove, 2); row.Children.Add(line.Label); row.Children.Add(line.Amount); row.Children.Add(remove); linePanel.Children.Add(row);
            }
        }
        Refresh();
        var add = Button("Adicionar renda", () => { lines.Add((Guid.NewGuid(), Input("", "Ex.: Salário", 40), Input("", "Ex.: 3000,00", 12))); Refresh(); }, icon: "Plus"); add.HorizontalAlignment = HorizontalAlignment.Left;
        var error = Text("", "error");
        var panel = Stack(Heading(person is null ? "Adicionar pessoa" : "Editar pessoa", $"Rendas de {month.ToString("MMMM 'de' yyyy", Money.Culture)}"), Field("Nome", name),
            Text("Rendas do mês (líquidas)", "section"), Text("Use uma linha para cada fonte: salário, extra, 13º. Use vírgula para centavos; 0 é válido. A renda do mês é a soma das linhas.", "muted"), linePanel, add, error);
        var dialog = Dialog("Pessoa e renda", panel); dialog.Height = 680;
        panel.Children.Add(Row(Button("Cancelar", () => dialog.Close()), Button("Salvar pessoa", () =>
        {
            try
            {
                var edited = new Person { Id = person?.Id ?? Guid.NewGuid(), Name = name.Text?.Trim() ?? "" };
                var incomes = lines.Select(l => new MonthlyIncome { Id = l.Id, PersonId = edited.Id, Month = month, Label = l.Label.Text?.Trim() ?? "", Amount = Money.Parse(l.Amount.Text, "Renda") }).ToList();
                Commit(d =>
                {
                    if (person is null) d.People.Add(edited); else d.People[d.People.FindIndex(p => p.Id == person.Id)] = edited;
                    // Substitui somente as linhas desta pessoa neste mês, preservando as outras competências.
                    d.Incomes.RemoveAll(i => i.PersonId == edited.Id && i.Month == month);
                    d.Incomes.AddRange(incomes);
                }); dialog.Close();
            }
            catch (Exception ex) { error.Text = FriendlyError(ex); error.BringIntoView(); }
        }, "primary")));
        await dialog.ShowDialog(this);
    }
}
