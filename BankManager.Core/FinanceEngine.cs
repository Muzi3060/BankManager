namespace BankManager.Core;

public sealed record Allocation(Dictionary<Guid, decimal> Amounts, string? Warning = null);
public sealed record PersonBalance(Person Person, decimal Income, decimal Assigned, decimal Paid)
{
    public decimal Remaining => Income - Assigned;
}
public sealed record Forecast(string Category, decimal Historical, decimal Scheduled, decimal Estimate, int Months);
public static class FinanceEngine
{
    public static decimal Income(BudgetData data, Guid person, DateOnly month) => data.Incomes.FirstOrDefault(i => i.PersonId == person && i.Month == Dates.Month(month))?.Amount ?? 0;
    public static List<Expense> Expenses(BudgetData data, DateOnly month) => data.Expenses.Where(e => Dates.Month(e.DueDate) == Dates.Month(month)).OrderBy(e => e.DueDate).ThenBy(e => e.Title).ToList();
    public static Allocation Allocate(BudgetData data, Expense expense)
    {
        var people = expense.Participants;
        if (people.Count == 0) throw new BudgetException("Nenhum responsável selecionado.");
        var weights = people.ToDictionary(id => id, _ => 1m);
        string? warning = null;
        if (expense.Split == SplitMode.AvailableIncome)
        {
            var monthly = Expenses(data, expense.DueDate);
            foreach (var id in people)
                weights[id] = Math.Max(0, Income(data, id, expense.DueDate) - monthly.Where(e => e.Split == SplitMode.Individual && e.Participants.Contains(id)).Sum(e => e.Amount));
            if (weights.Values.Sum() == 0)
            {
                warning = "Sem renda disponível entre os participantes: divisão igual aplicada. Confira as rendas e despesas individuais.";
                weights = people.ToDictionary(id => id, _ => 1m);
            }
        }
        var totalWeight = weights.Values.Sum();
        var cents = decimal.ToInt64(expense.Amount * 100);
        var raw = people.Select(id => (Id: id, Value: cents * weights[id] / totalWeight)).ToList();
        var result = raw.ToDictionary(p => p.Id, p => decimal.Floor(p.Value) / 100m);
        var remainder = (int)(cents - result.Values.Sum() * 100);
        foreach (var p in raw.OrderByDescending(p => p.Value - decimal.Floor(p.Value)).ThenBy(p => p.Id).Take(remainder)) result[p.Id] += .01m;
        return new Allocation(result, warning);
    }
    public static List<PersonBalance> Balances(BudgetData data, DateOnly month)
    {
        var allocations = Expenses(data, month).Select(e => (Expense: e, Allocation: Allocate(data, e))).ToList();
        return data.People.Select(p => new PersonBalance(p, Income(data, p.Id, month),
            allocations.Sum(a => a.Allocation.Amounts.GetValueOrDefault(p.Id)),
            allocations.Where(a => a.Expense.Paid).Sum(a => a.Allocation.Amounts.GetValueOrDefault(p.Id)))).ToList();
    }
    // Only completed months before target; missing months inside the observation window count as zero.
    public static List<Forecast> Forecasts(BudgetData data, DateOnly target)
    {
        target = Dates.Month(target);
        var first = data.Expenses.Where(e => e.DueDate < target && e.InstallmentCount == 1).Select(e => Dates.Month(e.DueDate)).DefaultIfEmpty(target).Min();
        var start = first > target.AddMonths(-3) ? first : target.AddMonths(-3);
        int months = (target.Year - start.Year) * 12 + target.Month - start.Month;
        return BudgetRules.Categories.Select(category =>
        {
            var history = data.Expenses.Where(e => e.Category == category && e.InstallmentCount == 1 && e.DueDate >= start && e.DueDate < target).Sum(e => e.Amount);
            var average = months == 0 ? 0 : decimal.Round(history / months, 2, MidpointRounding.AwayFromZero);
            var scheduled = Expenses(data, target).Where(e => e.Category == category).ToList();
            var installments = scheduled.Where(e => e.InstallmentCount > 1).Sum(e => e.Amount);
            var other = scheduled.Where(e => e.InstallmentCount == 1).Sum(e => e.Amount);
            return new Forecast(category, average, installments + other, installments + Math.Max(average, other), months);
        }).Where(f => f.Estimate > 0 || f.Historical > 0).ToList();
    }
}
