using System.Globalization;
using System.Text;

namespace BankManager.Core;

/// <summary>
/// Busca e filtros da lista de despesas, para um mês ou para todos. Não altera os dados.
/// </summary>
public static class ExpenseSearch
{
    /// <summary>
    /// Filtra as despesas. O texto procura no título e na categoria, sem diferenciar maiúsculas, minúsculas nem acentos
    /// ("saude" encontra "Saúde"). Com <paramref name="month"/>, devolve só aquele mês, por vencimento crescente; sem ele, devolve
    /// todos os meses, do vencimento mais recente ao mais antigo.
    /// </summary>
    /// <param name="data">Dados a consultar.</param>
    /// <param name="month">Mês (qualquer dia dele) ou nulo para todos os meses.</param>
    /// <param name="text">Texto procurado; vazio não filtra.</param>
    /// <param name="paid">Verdadeiro só pagas, falso só em aberto, nulo todas.</param>
    /// <param name="category">Categoria exata ou nulo para todas.</param>
    public static List<Expense> Run(BudgetData data, DateOnly? month, string? text, bool? paid = null, string? category = null)
    {
        var needle = Fold(text ?? "");
        var query = data.Expenses.Where(e => (month is null || Dates.Month(e.DueDate) == Dates.Month(month.Value))
            && (paid is null || e.Paid == paid) && (category is null || e.Category == category)
            && (needle.Length == 0 || Fold(e.Title).Contains(needle, StringComparison.Ordinal) || Fold(e.Category).Contains(needle, StringComparison.Ordinal)));
        return (month is null ? query.OrderByDescending(e => e.DueDate).ThenBy(e => e.Title) : query.OrderBy(e => e.DueDate).ThenBy(e => e.Title)).ToList();
    }
    /// <summary>
    /// Normaliza para comparação: minúsculas e sem acentos.
    /// </summary>
    internal static string Fold(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD)) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(char.ToLowerInvariant(c));
        return builder.ToString();
    }
}
