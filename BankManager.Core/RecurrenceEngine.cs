namespace BankManager.Core;

/// <summary>
/// Gera, encerra e remove despesas recorrentes. Trabalha sobre os dados recebidos (a interface passa uma cópia e só
/// adota o resultado depois de salvar) e não acessa disco nem controles.
/// </summary>
public static class RecurrenceEngine
{
    /// <summary>Quantos meses à frente do mês atual os lançamentos recorrentes são criados.</summary>
    public const int HorizonMonths = 12;
    /// <summary>
    /// Último mês criado automaticamente: o mês de <paramref name="today"/> mais <see cref="HorizonMonths"/>, limitado a dezembro de 2099
    /// (o limite da navegação do aplicativo).
    /// </summary>
    public static DateOnly Horizon(DateOnly today)
    {
        var horizon = Dates.Month(today).AddMonths(HorizonMonths);
        return horizon.Year > 2099 ? new DateOnly(2099, 12, 1) : horizon;
    }
    /// <summary>
    /// Cria os lançamentos que faltam, de cada recorrência, do mês inicial até o horizonte (ou o mês final, se houver).
    /// Pode ser chamado quantas vezes quiser: só cria o que não existe e respeita os meses excluídos de propósito.
    /// O mês inicial é sempre criado, mesmo que esteja além do horizonte.
    /// </summary>
    /// <returns>Quantidade de lançamentos criados.</returns>
    public static int Generate(BudgetData data, DateOnly today)
    {
        var horizon = Horizon(today); var created = 0;
        foreach (var recurrence in data.Recurrences)
        {
            var last = recurrence.StartMonth > horizon ? recurrence.StartMonth : horizon;
            if (recurrence.EndMonth is { } end && end < last) last = end;
            var existing = data.Expenses.Where(e => e.RecurrenceId == recurrence.Id).Select(e => e.RecurrenceMonth).ToHashSet();
            for (var month = recurrence.StartMonth; month <= last; month = month.AddMonths(1))
            {
                if (existing.Contains(month) || recurrence.SkippedMonths.Contains(month)) continue;
                // Meses curtos usam o último dia: dia 31 em fevereiro vence em 28 ou 29.
                var day = Math.Min(recurrence.DayOfMonth, DateTime.DaysInMonth(month.Year, month.Month));
                data.Expenses.Add(new Expense
                {
                    Title = recurrence.Title, Category = recurrence.Category, Amount = recurrence.Amount, DueDate = new DateOnly(month.Year, month.Month, day),
                    Split = recurrence.Split, Participants = [.. recurrence.Participants], RecurrenceId = recurrence.Id, RecurrenceMonth = month
                });
                created++;
            }
        }
        return created;
    }
    /// <summary>
    /// Encerra a recorrência: <paramref name="lastMonth"/> passa a ser o último mês gerado. Os lançamentos em aberto dos meses
    /// seguintes são removidos; os já pagos ficam no histórico.
    /// </summary>
    /// <exception cref="BudgetException">A recorrência não existe, já foi encerrada ou o mês é anterior ao início.</exception>
    public static void End(BudgetData data, Guid recurrenceId, DateOnly lastMonth)
    {
        var recurrence = data.Recurrences.FirstOrDefault(r => r.Id == recurrenceId) ?? throw new BudgetException("Recorrência não encontrada.");
        if (recurrence.EndMonth is not null) throw new BudgetException("Esta recorrência já foi encerrada.");
        lastMonth = Dates.Month(lastMonth);
        if (lastMonth < recurrence.StartMonth) throw new BudgetException("O último mês não pode ser anterior ao início da recorrência. Escolha um mês a partir de " + recurrence.StartMonth.ToString("MM/yyyy") + ".");
        recurrence.EndMonth = lastMonth;
        data.Expenses.RemoveAll(e => e.RecurrenceId == recurrenceId && !e.Paid && e.RecurrenceMonth > lastMonth);
    }
    /// <summary>
    /// Remove só o modelo, mantendo os lançamentos já criados como despesas comuns (nada do histórico é apagado).
    /// </summary>
    public static void Remove(BudgetData data, Guid recurrenceId)
    {
        if (data.Recurrences.RemoveAll(r => r.Id == recurrenceId) == 0) throw new BudgetException("Recorrência não encontrada.");
        foreach (var expense in data.Expenses.Where(e => e.RecurrenceId == recurrenceId)) { expense.RecurrenceId = null; expense.RecurrenceMonth = null; }
    }
    /// <summary>
    /// Exclui um lançamento. Se ele veio de uma recorrência, registra o mês como ignorado para que a geração automática não o recrie.
    /// </summary>
    public static void RemoveExpense(BudgetData data, Guid expenseId)
    {
        var expense = data.Expenses.FirstOrDefault(e => e.Id == expenseId); if (expense is null) return;
        data.Expenses.Remove(expense);
        if (expense.RecurrenceId is { } id && expense.RecurrenceMonth is { } month && data.Recurrences.FirstOrDefault(r => r.Id == id) is { } recurrence && !recurrence.SkippedMonths.Contains(month))
            recurrence.SkippedMonths.Add(month);
    }
}
