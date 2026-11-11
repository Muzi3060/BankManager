namespace BankManager.Core;

/// <summary>
/// Quanto de uma meta já foi usado. <paramref name="Ratio"/> é gasto dividido pela meta (1 = 100%).
/// </summary>
/// <param name="Category">Categoria da meta.</param>
/// <param name="Spent">Total das despesas do mês na categoria, pagas ou em aberto.</param>
/// <param name="Goal">Limite mensal.</param>
/// <param name="Ratio">Gasto dividido pela meta; pode passar de 1.</param>
public sealed record GoalProgress(string Category, decimal Spent, decimal Goal, decimal Ratio)
{
    /// <summary>A partir de que fração da meta o aviso "perto do limite" aparece.</summary>
    public const decimal NearRatio = 0.8m;
    /// <summary>Situação da meta: dentro do limite, perto dele (80% a 100%) ou acima.</summary>
    public GoalLevel Level => Ratio > 1 ? GoalLevel.Over : Ratio >= NearRatio ? GoalLevel.Near : GoalLevel.Ok;
}
/// <summary>
/// Situação de uma meta em um mês.
/// </summary>
public enum GoalLevel
{
    /// <summary>Abaixo de 80% da meta.</summary>
    Ok,
    /// <summary>Entre 80% e 100% da meta, inclusive.</summary>
    Near,
    /// <summary>Acima da meta.</summary>
    Over
}
/// <summary>
/// Define e acompanha metas mensais por categoria. Trabalha sobre os dados recebidos e não acessa disco.
/// </summary>
public static class GoalEngine
{
    /// <summary>
    /// Define (ou troca) a meta de uma categoria existente.
    /// </summary>
    public static void Set(BudgetData data, string category, decimal amount)
    {
        if (!BudgetRules.AllCategories(data).Contains(category)) throw new BudgetException("Categoria inválida.");
        Money.Validate(amount, "Meta", true);
        var goal = data.Goals.FirstOrDefault(g => g.Category == category);
        if (goal is null) data.Goals.Add(new CategoryGoal { Category = category, Amount = amount }); else goal.Amount = amount;
    }
    /// <summary>
    /// Remove a meta da categoria, se houver.
    /// </summary>
    public static void Clear(BudgetData data, string category) => data.Goals.RemoveAll(g => g.Category == category);
    /// <summary>
    /// Progresso de todas as metas no mês, na ordem das categorias; categorias sem gasto aparecem com gasto zero.
    /// </summary>
    public static List<GoalProgress> Progress(BudgetData data, DateOnly month)
    {
        var spent = FinanceEngine.Expenses(data, month).GroupBy(e => e.Category).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
        var order = BudgetRules.AllCategories(data);
        return data.Goals.OrderBy(g => order.IndexOf(g.Category)).Select(g => new GoalProgress(g.Category, spent.GetValueOrDefault(g.Category), g.Amount, spent.GetValueOrDefault(g.Category) / g.Amount)).ToList();
    }
}
