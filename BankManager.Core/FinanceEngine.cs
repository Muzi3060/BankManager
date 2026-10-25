namespace BankManager.Core;

/// <summary>
/// Resultado do rateio: valor por identificador de pessoa e aviso quando foi necessário usar divisão igual.
/// </summary>
/// <param name="Amounts">Valores em reais cuja soma corresponde ao lançamento.</param>
/// <param name="Warning">Explicação do fallback por falta de renda disponível; nulo quando não houve fallback.</param>
public sealed record Allocation(Dictionary<Guid, decimal> Amounts, string? Warning = null);
/// <summary>
/// Resumo mensal de uma pessoa, calculado a partir das despesas e da renda da competência.
/// </summary>
/// <param name="Person">Pessoa à qual o resumo pertence.</param>
/// <param name="Income">Renda líquida do mês.</param>
/// <param name="Assigned">Total de responsabilidades, pagas ou em aberto.</param>
/// <param name="Paid">Responsabilidade em lançamentos marcados como integralmente pagos.</param>
public sealed record PersonBalance(Person Person, decimal Income, decimal Assigned, decimal Paid)
{
    /// <summary>
    /// Renda menos todas as responsabilidades do mês; pode ser negativa e não equivale a saldo bancário.
    /// </summary>
    public decimal Remaining => Income - Assigned;
}
/// <summary>
/// Estimativa de uma categoria, com histórico e compromissos separados para explicar o cálculo.
/// </summary>
/// <param name="Category">Categoria analisada.</param>
/// <param name="Historical">Média mensal das despesas não parceladas na janela observada.</param>
/// <param name="Scheduled">Total de lançamentos, à vista e parcelados, já cadastrados no mês alvo.</param>
/// <param name="Estimate">Maior valor entre média e lançamentos à vista, acrescido das parcelas do mês alvo.</param>
/// <param name="Months">Tamanho da janela histórica em meses, entre zero e três, incluindo meses sem lançamentos.</param>
public sealed record Forecast(string Category, decimal Historical, decimal Scheduled, decimal Estimate, int Months);
/// <summary>
/// Consultas e cálculos financeiros sem acesso a disco ou controles da interface. Não modifica os dados recebidos.
/// </summary>
public static class FinanceEngine
{
    /// <summary>
    /// Consulta a renda da pessoa na competência; ausência de registro é tratada como zero, sem copiar outro mês.
    /// </summary>
    public static decimal Income(BudgetData data, Guid person, DateOnly month) => data.Incomes.FirstOrDefault(i => i.PersonId == person && i.Month == Dates.Month(month))?.Amount ?? 0;
    /// <summary>
    /// Obtém lançamentos do mês selecionado, ordenados por vencimento e depois por descrição.
    /// </summary>
    public static List<Expense> Expenses(BudgetData data, DateOnly month) => data.Expenses.Where(e => Dates.Month(e.DueDate) == Dates.Month(month)).OrderBy(e => e.DueDate).ThenBy(e => e.Title).ToList();
    /// <summary>
    /// Rateia uma despesa validada pelos responsáveis e distribui os centavos pelos maiores restos fracionários.
    /// </summary>
    /// <remarks>Despesas individuais, inclusive pagas, reduzem os pesos proporcionais. Se todos os pesos forem zero, aplica divisão igual com aviso.</remarks>
    public static Allocation Allocate(BudgetData data, Expense expense)
    {
        var people = expense.Participants;
        if (people.Count == 0) throw new BudgetException("Nenhum responsável selecionado.");
        // Pesos iguais atendem ao modo igual e ao individual (que possui apenas um responsável).
        var weights = people.ToDictionary(id => id, _ => 1m);
        string? warning = null;
        if (expense.Split == SplitMode.AvailableIncome)
        {
            // Calcula uma base fixa para todo o mês: despesas compartilhadas não são descontadas recursivamente.
            var monthly = Expenses(data, expense.DueDate);
            foreach (var id in people)
                weights[id] = Math.Max(0, Income(data, id, expense.DueDate) - monthly.Where(e => e.Split == SplitMode.Individual && e.Participants.Contains(id)).Sum(e => e.Amount));
            // Evita divisão por zero e torna explícita a decisão quando todos estão sem renda disponível.
            if (weights.Values.Sum() == 0)
            {
                warning = "Sem renda disponível entre os participantes: divisão igual aplicada. Confira as rendas e despesas individuais.";
                weights = people.ToDictionary(id => id, _ => 1m);
            }
        }
        var totalWeight = weights.Values.Sum();
        var cents = decimal.ToInt64(expense.Amount * 100);
        // Primeiro arredonda cada cota para baixo em centavos; a soma pode deixar alguns centavos sem destino.
        var raw = people.Select(id => (Id: id, Value: cents * weights[id] / totalWeight)).ToList();
        var result = raw.ToDictionary(p => p.Id, p => decimal.Floor(p.Value) / 100m);
        var remainder = (int)(cents - result.Values.Sum() * 100);
        // Os maiores restos recebem um centavo extra. O Guid desempata de forma estável dentro da mesma base.
        foreach (var p in raw.OrderByDescending(p => p.Value - decimal.Floor(p.Value)).ThenBy(p => p.Id).Take(remainder)) result[p.Id] += .01m;
        return new Allocation(result, warning);
    }
    /// <summary>
    /// Soma as cotas de cada pessoa no mês e separa o que está quitado, sem descontar o pago duas vezes.
    /// </summary>
    public static List<PersonBalance> Balances(BudgetData data, DateOnly month)
    {
        var allocations = Expenses(data, month).Select(e => (Expense: e, Allocation: Allocate(data, e))).ToList();
        return data.People.Select(p => new PersonBalance(p, Income(data, p.Id, month),
            allocations.Sum(a => a.Allocation.Amounts.GetValueOrDefault(p.Id)),
            allocations.Where(a => a.Expense.Paid).Sum(a => a.Allocation.Amounts.GetValueOrDefault(p.Id)))).ToList();
    }
    /// <summary>
    /// Estima gastos por categoria usando até três meses anteriores ao alvo e os compromissos já cadastrados.
    /// </summary>
    /// <remarks>A janela não inclui o mês alvo. Meses vazios dentro dela contam como zero; um mês anterior ainda em andamento pode conter histórico parcial.</remarks>
    public static List<Forecast> Forecasts(BudgetData data, DateOnly target)
    {
        target = Dates.Month(target);
        // A janela começa no primeiro mês com compra não parcelada, limitada aos três meses mais recentes.
        var first = data.Expenses.Where(e => e.DueDate < target && e.InstallmentCount == 1).Select(e => Dates.Month(e.DueDate)).DefaultIfEmpty(target).Min();
        var start = first > target.AddMonths(-3) ? first : target.AddMonths(-3);
        int months = (target.Year - start.Year) * 12 + target.Month - start.Month;
        return BudgetRules.Categories.Select(category =>
        {
            // Parcelas não entram na média para que uma dívida encerrada não seja projetada indefinidamente.
            var history = data.Expenses.Where(e => e.Category == category && e.InstallmentCount == 1 && e.DueDate >= start && e.DueDate < target).Sum(e => e.Amount);
            var average = months == 0 ? 0 : decimal.Round(history / months, 2, MidpointRounding.AwayFromZero);
            var scheduled = Expenses(data, target).Where(e => e.Category == category).ToList();
            var installments = scheduled.Where(e => e.InstallmentCount > 1).Sum(e => e.Amount);
            var other = scheduled.Where(e => e.InstallmentCount == 1).Sum(e => e.Amount);
            // O máximo evita somar novamente uma compra à vista já prevista na média. Parcelas são compromissos adicionais.
            return new Forecast(category, average, installments + other, installments + Math.Max(average, other), months);
        }).Where(f => f.Estimate > 0 || f.Historical > 0).ToList();
    }
}
