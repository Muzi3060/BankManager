using Avalonia;
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
            var informed = data.Incomes.Any(i => i.PersonId == person.Id && i.Month == month);
            var who = Stack(new TextBlock { Text = person.Name, FontWeight = FontWeight.Medium }, Text(informed ? "Renda informada neste mês" : "Renda ainda não informada neste mês", informed ? "muted" : "warn")); who.Spacing = 0; who.VerticalAlignment = VerticalAlignment.Center;
            var avatar = Avatar(person.Name); avatar.Margin = new Thickness(0, 0, 12, 0);
            var income = Text(Money.Format(FinanceEngine.Income(data, person.Id, month)), "num"); income.FontWeight = FontWeight.SemiBold; income.VerticalAlignment = VerticalAlignment.Center; income.Margin = new Thickness(16, 0);
            var edit = Button("Editar", () => _ = Run(() => EditPerson(person)));
            var delete = Button("Excluir pessoa", () => _ = Run(async () =>
            {
                // Não permite apagar referências utilizadas em nenhum mês, evitando despesas sem responsável.
                if (data.Expenses.Any(e => e.Participants.Contains(person.Id))) throw new BudgetException("Esta pessoa possui despesas no histórico. Remova ou reatribua essas despesas antes de excluí-la.");
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
        if (await Confirm(this, "Copiar rendas?", $"Preencher {previous.Count} renda(s) ainda não informada(s) com os valores do mês anterior?")) Commit(d => d.Incomes.AddRange(previous.Select(i => i with { Month = month })));
    }
    /// <summary>
    /// Abre o cadastro ou edição de pessoa. O nome é global, mas a renda salva pertence apenas ao mês selecionado.
    /// </summary>
    private async Task EditPerson(Person? person = null)
    {
        var name = Input(person?.Name, "Ex.: Ana", 80);
        var amount = Input(person is null ? "" : FinanceEngine.Income(data, person.Id, month).ToString("0.00", Money.Culture), "Ex.: 3000,00", 12);
        var error = Text("", "error");
        var panel = Stack(Heading(person is null ? "Adicionar pessoa" : "Editar pessoa", $"Renda de {month.ToString("MMMM 'de' yyyy", Money.Culture)}"), Field("Nome", name), Field("Renda líquida do mês (R$)", amount, "Use vírgula para centavos. Informe 0 se não houver renda neste mês."), error);
        var dialog = Dialog("Pessoa e renda", panel); dialog.Height = 540;
        panel.Children.Add(Row(Button("Cancelar", () => dialog.Close()), Button("Salvar pessoa", () =>
        {
            try
            {
                var income = Money.Parse(amount.Text, "Renda"); var edited = new Person { Id = person?.Id ?? Guid.NewGuid(), Name = name.Text?.Trim() ?? "" };
                Commit(d =>
                {
                    if (person is null) d.People.Add(edited); else d.People[d.People.FindIndex(p => p.Id == person.Id)] = edited;
                    // Substitui somente a chave pessoa/mês atual, preservando rendas das outras competências.
                    d.Incomes.RemoveAll(i => i.PersonId == edited.Id && i.Month == month);
                    d.Incomes.Add(new MonthlyIncome { PersonId = edited.Id, Month = month, Amount = income });
                }); dialog.Close();
            }
            catch (Exception ex) { error.Text = FriendlyError(ex); error.BringIntoView(); }
        }, "primary")));
        await dialog.ShowDialog(this);
    }
}
