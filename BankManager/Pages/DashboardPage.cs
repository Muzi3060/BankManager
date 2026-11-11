using Avalonia.Controls;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Página "Visão geral": resumo do mês, divisão por pessoa e próximos pagamentos.
public sealed partial class MainWindow
{
    /// <summary>
    /// Monta o resumo do mês, a orientação inicial, o alerta de rendas ausentes e os próximos pagamentos em aberto.
    /// </summary>
    private Control Dashboard()
    {
        var expenses = FinanceEngine.Expenses(data, month); var income = data.People.Sum(p => FinanceEngine.Income(data, p.Id, month)); var total = expenses.Sum(e => e.Amount);
        var result = Stack(Heading("Seu mês, com clareza", "Acompanhe o que entra, o que precisa pagar e quanto sobra."),
            Columns(Metric("Renda da casa", income, $"{data.People.Count} pessoa(s) cadastrada(s)", "accent"), Metric("Despesas do mês", total, $"{Money.Format(expenses.Where(e => !e.Paid).Sum(e => e.Amount))} em aberto"), Metric("Saldo planejado", income - total, "Renda menos todas as despesas", income >= total ? "positive" : "negative")),
            Row(Button("Nova despesa", () => _ = Run(() => EditExpense()), "primary", "Plus"), Button("Gerenciar pessoas", () => { currentPage = 1; Render(); })));
        if (data.People.Count == 0)
        {
            result.Children.Add(Card(Stack(Text("Vamos organizar suas finanças?", "section"), Text("1. Cadastre quem faz parte da casa e a renda deste mês.\n2. Adicione despesas individuais ou compartilhadas.\n3. Veja a divisão e acompanhe o pagamento.", "muted"), Button("Cadastrar primeira pessoa", () => _ = Run(() => EditPerson()), "primary"))));
            return result;
        }
        // Registro ausente é diferente de renda explicitamente zero; ambos entram como zero no cálculo.
        var missing = data.People.Where(p => !data.Incomes.Any(i => i.PersonId == p.Id && i.Month == month)).ToList();
        if (missing.Count > 0) result.Children.Add(Card(Text("Renda ainda não informada neste mês: " + string.Join(", ", missing.Select(p => p.Name)) + ". Informe a renda em Pessoas e rendas; até lá, ela será considerada zero.", "muted")));
        result.Children.Add(BalanceCard());
        var categoryPanel = Stack(Text("Para onde vai o dinheiro", "section"));
        if (expenses.Count == 0) categoryPanel.Children.Add(Text("Nenhuma despesa neste mês. Use “Nova despesa” para começar.", "muted"));
        foreach (var g in expenses.GroupBy(e => e.Category).OrderByDescending(g => g.Sum(e => e.Amount)))
        {
            var amount = g.Sum(e => e.Amount);
            // A cor da barra vem do estilo ProgressBar (token de destaque), não do código.
            categoryPanel.Children.Add(Stack(Text($"{g.Key}  ·  {Money.Format(amount)}"), new ProgressBar { Value = (double)(amount / total * 100), Maximum = 100, Height = 7 }));
        }
        result.Children.Add(Card(categoryPanel));
        var upcoming = expenses.Where(e => !e.Paid).Take(5).ToList();
        if (upcoming.Count > 0)
        {
            result.Children.Add(Text("Próximos pagamentos", "section"));
            foreach (var e in upcoming) result.Children.Add(ExpenseCard(e));
        }
        return result;
    }
    /// <summary>
    /// Apresenta responsabilidades, quitação e sobra por pessoa, além de avisos de rateio sem renda disponível.
    /// </summary>
    private Control BalanceCard()
    {
        var panel = Stack(Text("Uma divisão que considera cada pessoa", "section"), Text("Disponível para dividir = renda do mês − despesas individuais. Despesas compartilhadas proporcionais usam esse valor como peso.", "muted"));
        foreach (var b in FinanceEngine.Balances(data, month))
        {
            panel.Children.Add(Columns(Stack(Text(b.Person.Name, "section"), Text($"Renda {Money.Format(b.Income)}", "muted")),
                Stack(Text($"Responsabilidade: {Money.Format(b.Assigned)}"), Text($"Quitado: {Money.Format(b.Paid)}", "muted")),
                Stack(Text($"Sobra: {Money.Format(b.Remaining)}"), Text(b.Remaining < 0 ? "Atenção: despesas acima da renda" : "Após todas as despesas do mês", "muted"))));
        }
        foreach (var warning in FinanceEngine.Expenses(data, month).Select(e => FinanceEngine.Allocate(data, e).Warning).Where(w => w is not null).Distinct()) panel.Children.Add(Text(warning!, "muted"));
        return Card(panel);
    }
}
