namespace BankManager.Core;

/// <summary>
/// Situação de uma despesa em relação ao seu vencimento. Serve de base para os chips da interface.
/// </summary>
public enum DueKind
{
    /// <summary>A despesa já foi quitada.</summary>
    Paid,
    /// <summary>Em aberto, com vencimento ainda distante.</summary>
    Open,
    /// <summary>Em aberto e vence hoje ou nos próximos dias (veja <see cref="DueStatus.SoonDays"/>).</summary>
    DueSoon,
    /// <summary>Em aberto e já passou do vencimento.</summary>
    Overdue
}
/// <summary>
/// Resultado de <see cref="Of"/>: a categoria da situação e quantos dias faltam (negativo se atrasada).
/// </summary>
/// <param name="Kind">Categoria da situação.</param>
/// <param name="Days">Dias até o vencimento: 0 é hoje, negativo é atraso. Sem sentido para despesas pagas.</param>
public readonly record struct DueStatus(DueKind Kind, int Days)
{
    /// <summary>Até quantos dias antes do vencimento a despesa passa a ser destacada como "vence em breve".</summary>
    public const int SoonDays = 3;
    /// <summary>
    /// Calcula a situação de uma despesa. A data de referência é recebida como parâmetro para que
    /// o cálculo seja determinístico nos testes (a interface passa o dia de hoje).
    /// </summary>
    public static DueStatus Of(Expense expense, DateOnly today)
    {
        var days = expense.DueDate.DayNumber - today.DayNumber;
        if (expense.Paid) return new(DueKind.Paid, days);
        return new(days < 0 ? DueKind.Overdue : days <= SoonDays ? DueKind.DueSoon : DueKind.Open, days);
    }
}
