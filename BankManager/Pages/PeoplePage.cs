using Avalonia.Controls;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Página "Pessoas e rendas": cadastro de quem divide as despesas e da renda de cada mês.
public sealed partial class MainWindow
{
    /// <summary>
    /// Lista pessoas e renda da competência, com edição e exclusão protegida por referências no histórico.
    /// </summary>
    private Control PeoplePage()
    {
        var panel = Stack(Heading("Pessoas e rendas", "Informe a renda líquida: o dinheiro que realmente entra no mês selecionado."),
            Row(Button("Adicionar pessoa", () => _ = Run(() => EditPerson()), "primary", "Plus"), Button("Copiar rendas do mês anterior", () => _ = Run(CopyIncome))),
            Text("Cada mês guarda sua própria renda. Alterar o nome vale para todo o histórico; alterar a renda afeta apenas este mês e recalcula sua divisão.", "muted"));
        if (data.People.Count == 0) panel.Children.Add(Card(Text("Adicione você e as pessoas que compartilham as despesas.", "muted")));
        foreach (var person in data.People)
        {
            var income = FinanceEngine.Income(data, person.Id, month);
            panel.Children.Add(Card(Stack(Columns(Stack(Text(person.Name, "section"), Text(data.Incomes.Any(i => i.PersonId == person.Id && i.Month == month) ? "Renda informada neste mês" : "Renda não informada neste mês", "muted")), Text(Money.Format(income), "money")),
                Row(Button("Editar pessoa / renda", () => _ = Run(() => EditPerson(person))), Button("Excluir pessoa", () => _ = Run(async () =>
                {
                    // Não permite apagar referências utilizadas em nenhum mês, evitando despesas sem responsável.
                    if (data.Expenses.Any(e => e.Participants.Contains(person.Id))) throw new BudgetException("Esta pessoa possui despesas no histórico. Remova ou reatribua essas despesas antes de excluí-la.");
                    if (await Confirm(this, "Excluir pessoa?", $"Excluir {person.Name} e todos os seus registros de renda?")) Commit(d => { d.People.RemoveAll(p => p.Id == person.Id); d.Incomes.RemoveAll(i => i.PersonId == person.Id); });
                }), "danger")))));
        }
        if (data.People.Count > 0) panel.Children.Add(BalanceCard());
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
