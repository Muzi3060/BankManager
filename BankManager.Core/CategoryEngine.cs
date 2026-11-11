namespace BankManager.Core;

/// <summary>
/// Gerencia as categorias criadas pelo usuário. As categorias padrão (<see cref="BudgetRules.Categories"/>) são fixas e
/// nunca são alteradas aqui. Trabalha sobre os dados recebidos e não acessa disco.
/// </summary>
public static class CategoryEngine
{
    /// <summary>Quantidade máxima de categorias próprias.</summary>
    public const int MaxCustom = 40;
    /// <summary>Tamanho máximo do nome de uma categoria.</summary>
    public const int MaxLength = 30;
    /// <summary>
    /// Cria uma categoria. Rejeita nome vazio, longo, com caracteres de controle ou já existente (ignorando maiúsculas e minúsculas).
    /// </summary>
    public static void Add(BudgetData data, string name)
    {
        name = Clean(name);
        if (Exists(data, name)) throw new BudgetException("Já existe uma categoria com esse nome.");
        if (data.CustomCategories.Count >= MaxCustom) throw new BudgetException($"Você já tem {MaxCustom} categorias próprias. Exclua alguma antes de criar outra.");
        data.CustomCategories.Add(name);
    }
    /// <summary>
    /// Renomeia uma categoria própria e atualiza, de uma vez, todas as despesas e recorrências que a usam.
    /// Trocar só maiúsculas e minúsculas (por exemplo "pets" para "Pets") é permitido.
    /// </summary>
    public static void Rename(BudgetData data, string oldName, string newName)
    {
        var index = data.CustomCategories.IndexOf(oldName);
        if (index < 0) throw new BudgetException("As categorias padrão não podem ser renomeadas.");
        newName = Clean(newName);
        if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase) && Exists(data, newName)) throw new BudgetException("Já existe uma categoria com esse nome.");
        data.CustomCategories[index] = newName;
        foreach (var expense in data.Expenses.Where(e => e.Category == oldName)) expense.Category = newName;
        foreach (var recurrence in data.Recurrences.Where(r => r.Category == oldName)) recurrence.Category = newName;
    }
    /// <summary>
    /// Exclui uma categoria própria, desde que nenhuma despesa nem recorrência a use.
    /// </summary>
    public static void Remove(BudgetData data, string name)
    {
        if (!data.CustomCategories.Contains(name)) throw new BudgetException("As categorias padrão não podem ser excluídas.");
        var expenses = data.Expenses.Count(e => e.Category == name); var recurrences = data.Recurrences.Count(r => r.Category == name);
        if (expenses + recurrences > 0) throw new BudgetException($"Esta categoria é usada por {expenses} despesa(s) e {recurrences} recorrência(s). Mude a categoria delas antes de excluí-la.");
        data.CustomCategories.Remove(name);
    }
    private static string Clean(string? name)
    {
        name = name?.Trim() ?? ""; BudgetRules.Text(name, "Nome da categoria", MaxLength); return name;
    }
    private static bool Exists(BudgetData data, string name) => BudgetRules.AllCategories(data).Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
}
