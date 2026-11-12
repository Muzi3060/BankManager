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
/// Total de despesas de um mês, usado pelo gráfico da visão geral.
/// </summary>
/// <param name="Month">Primeiro dia do mês.</param>
/// <param name="Total">Soma de todas as despesas com vencimento no mês, pagas ou em aberto; zero quando não há nenhuma.</param>
public sealed record MonthTotal(DateOnly Month, decimal Total);
/// <summary>
/// Uma entrada ou saída de dinheiro mostrada em "Últimas movimentações".
/// </summary>
/// <param name="Date">Data da movimentação: o vencimento da despesa paga ou o dia 1 do mês para rendas (a renda só tem mês).</param>
/// <param name="Title">Descrição da despesa ou nome da linha de renda (por exemplo "Salário").</param>
/// <param name="Category">Categoria da despesa ou, nas rendas, "Renda de" seguido do nome da pessoa.</param>
/// <param name="Amount">Valor sempre positivo; <paramref name="IsIncome"/> indica o sentido.</param>
/// <param name="IsIncome">Verdadeiro para entradas (rendas), falso para saídas (despesas pagas).</param>
public sealed record Movement(DateOnly Date, string Title, string Category, decimal Amount, bool IsIncome);
/// <summary>
/// Consultas e cálculos financeiros sem acesso a disco ou controles da interface. Não modifica os dados recebidos.
/// </summary>
public static class FinanceEngine
{
    /// <summary>
    /// Consulta a renda da pessoa na competência (soma de todas as suas linhas); ausência de registro é tratada como zero, sem copiar outro mês.
    /// </summary>
    public static decimal Income(BudgetData data, Guid person, DateOnly month) => IncomeLines(data, person, month).Sum(i => i.Amount);
    /// <summary>
    /// Linhas de renda da pessoa na competência, na ordem em que foram cadastradas.
    /// </summary>
    public static List<MonthlyIncome> IncomeLines(BudgetData data, Guid person, DateOnly month) => data.Incomes.Where(i => i.PersonId == person && i.Month == Dates.Month(month)).ToList();
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
        return BudgetRules.AllCategories(data).Select(category =>
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
    /// <summary>
    /// Totais de despesas dos últimos <paramref name="count"/> meses, terminando no mês informado (inclusive), do mais antigo ao mais recente.
    /// </summary>
    /// <remarks>Meses sem despesas aparecem com total zero, para o gráfico manter sempre o mesmo número de barras.</remarks>
    public static List<MonthTotal> MonthlyTotals(BudgetData data, DateOnly month, int count)
    {
        if (count is < 1 or > 120) throw new ArgumentOutOfRangeException(nameof(count));
        var last = Dates.Month(month);
        var totals = data.Expenses.GroupBy(e => Dates.Month(e.DueDate)).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
        return Enumerable.Range(0, count).Select(i => last.AddMonths(i - count + 1)).Select(m => new MonthTotal(m, totals.GetValueOrDefault(m))).ToList();
    }
    /// <summary>
    /// Movimentações do mês, das mais recentes para as mais antigas, limitadas a <paramref name="limit"/> itens.
    /// </summary>
    /// <remarks>
    /// Entram as rendas do mês (com data no dia 1; linhas de valor zero ficam de fora) e as despesas já marcadas como pagas.
    /// Como a despesa não guarda a data do pagamento, vale o vencimento. Em datas iguais a ordem é estável: primeiro as
    /// despesas (por descrição), depois as rendas (na ordem de cadastro).
    /// </remarks>
    public static List<Movement> RecentMovements(BudgetData data, DateOnly month, int limit = 6)
    {
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        var first = Dates.Month(month);
        var paid = Expenses(data, first).Where(e => e.Paid).Select(e => new Movement(e.DueDate, e.Title, e.Category, e.Amount, false));
        var incomes = data.Incomes.Where(i => i.Month == first && i.Amount > 0)
            .Select(i => new Movement(first, i.Label, "Renda de " + (data.People.FirstOrDefault(p => p.Id == i.PersonId)?.Name ?? "—"), i.Amount, true));
        // OrderByDescending é estável: itens da mesma data mantêm a ordem em que foram concatenados.
        return paid.Concat(incomes).OrderByDescending(m => m.Date).Take(limit).ToList();
    }
}
