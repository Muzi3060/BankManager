using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Página "Pessoas e rendas": cadastro de quem divide as despesas, a renda de cada mês e quanto cada pessoa assume das despesas.
public sealed partial class MainWindow
{
    /// <summary>
    /// Lista pessoas em linhas: renda da competência, parte das despesas que cada uma assume e quanto sobra, com edição e exclusão
    /// protegida por referências no histórico.
    /// </summary>
    private Control PeoplePage()
    {
        var actions = Row(Button("Adicionar pessoa", () => _ = Run(() => EditPerson()), "ink", "Plus"), Button("Copiar rendas do mês anterior", () => _ = Run(CopyIncome)));
        var panel = Stack(PageHeader("Pessoas e rendas", "Quem divide as despesas, quanto cada pessoa recebe e quanto assume neste mês."), actions); panel.Spacing = 18;
        if (data.People.Count == 0) { panel.Children.Add(EmptyCard("Ninguém por aqui ainda", "Adicione você e as pessoas que compartilham as despesas. Depois informe a renda de cada uma neste mês.")); return panel; }
        var balances = FinanceEngine.Balances(data, month).ToDictionary(b => b.Person.Id);
        var rows = new StackPanel();
        foreach (var person in data.People)
        {
            if (rows.Children.Count > 0) rows.Children.Add(Divider());
            // Resumo das rendas do mês: nenhuma (alerta), uma (só o nome) ou várias ("Salário R$ 3.000,00 · Extra R$ 500,00").
            var lines = FinanceEngine.IncomeLines(data, person.Id, month);
            var status = lines.Count == 0 ? "Renda ainda não informada neste mês" : lines.Count == 1 ? lines[0].Label : string.Join(" · ", lines.Select(l => $"{l.Label} {Money.Format(l.Amount)}"));
            var who = Stack(new TextBlock { Text = person.Name, FontWeight = FontWeight.SemiBold }, Text(status, lines.Count == 0 ? "warn" : "muted").WithClass("small")); who.Spacing = 1; who.VerticalAlignment = VerticalAlignment.Center; who.Margin = new Thickness(12, 0);
            // Divisão do mês: quanto a pessoa assume das despesas e quanto isso compromete da renda dela.
            var b = balances[person.Id]; var committed = b.Income > 0 ? (double)Math.Min(100, b.Assigned / b.Income * 100) : 0;
            var share = Stack(Text(b.Income > 0 ? $"Assume {Money.Format(b.Assigned)} · {committed:0}% da renda" : $"Assume {Money.Format(b.Assigned)}", "muted").WithClass("small"), new ProgressBar { Value = committed, Maximum = 100, Height = 6 }.WithClass("neutral")); share.Spacing = 6; share.VerticalAlignment = VerticalAlignment.Center; share.Margin = new Thickness(0, 0, 20, 0);
            var remaining = Text($"Sobra {Money.Format(b.Remaining)}", b.Remaining < 0 ? "negative" : "positive"); remaining.Classes.Add("small"); remaining.TextAlignment = TextAlignment.Right;
            var incomeValue = Text(Money.Format(b.Income), "strong"); incomeValue.TextAlignment = TextAlignment.Right;
            var income = Stack(incomeValue, remaining); income.Spacing = 1; income.VerticalAlignment = VerticalAlignment.Center; income.Margin = new Thickness(0, 0, 16, 0); income.MinWidth = 130;
            var edit = Button("Editar", () => _ = Run(() => EditPerson(person)));
            var delete = Button("Excluir pessoa", () => _ = Run(async () =>
            {
                // Não permite apagar referências utilizadas em nenhum mês, evitando despesas sem responsável.
                if (data.Expenses.Any(e => e.Participants.Contains(person.Id))) throw new BudgetException("Esta pessoa possui despesas no histórico. Remova ou reatribua essas despesas antes de excluí-la.");
                if (data.Expenses.Any(e => e.PaidBy == person.Id) || data.Settlements.Any(s => s.PayerId == person.Id || s.ReceiverId == person.Id)) throw new BudgetException("Esta pessoa aparece como quem pagou uma despesa ou em um acerto de contas. Remova esses registros antes de excluí-la.");
                if (data.Recurrences.Any(r => r.Participants.Contains(person.Id))) throw new BudgetException("Esta pessoa participa de uma despesa recorrente. Em Despesas → Recorrentes, encerre e remova a recorrência antes de excluí-la.");
                if (await Confirm(this, "Excluir pessoa?", $"Excluir {person.Name} e todos os seus registros de renda?")) Commit(d => { d.People.RemoveAll(p => p.Id == person.Id); d.Incomes.RemoveAll(i => i.PersonId == person.Id); });
            }), "ghost round danger", "Trash", iconOnly: true);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center }; buttons.Children.Add(edit); buttons.Children.Add(delete);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,230,Auto,Auto"), Margin = new Thickness(0, 12) };
            Control[] cells = [Avatar(person.Name), who, share, income, buttons];
            for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); row.Children.Add(cells[i]); }
            rows.Children.Add(row);
        }
        panel.Children.Add(Card(rows));
        // Avisos do rateio (por exemplo, divisão igual por falta de renda disponível) aparecem uma vez cada.
        foreach (var warning in FinanceEngine.Expenses(data, month).Select(e => FinanceEngine.Allocate(data, e).Warning).Where(w => w is not null).Distinct()) panel.Children.Add(InlineNotice(warning!));
        panel.Children.Add(Text("Cada mês guarda sua própria renda. Alterar o nome vale para todo o histórico; alterar a renda afeta apenas este mês e recalcula a divisão dele. Na divisão proporcional, quem tem mais renda disponível assume uma parte maior.", "muted"));
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
                var remove = Button("Remover renda", () => { lines.Remove(line); Refresh(); }, "ghost round danger", "Trash", iconOnly: true);
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
