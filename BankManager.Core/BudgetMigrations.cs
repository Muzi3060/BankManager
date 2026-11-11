namespace BankManager.Core;

/// <summary>
/// Atualiza dados lidos de arquivos antigos para o esquema atual, sem perder nada. Cada versão tem um passo próprio,
/// executado em ordem, de modo que um arquivo muito antigo passa por todos os passos até chegar à versão atual.
/// </summary>
public static class BudgetMigrations
{
    /// <summary>
    /// Leva o orçamento à <see cref="BudgetData.CurrentVersion"/>. Versões desconhecidas (inclusive futuras) são recusadas.
    /// </summary>
    /// <exception cref="BudgetException">A versão não é suportada ou o arquivo mistura campos de versões diferentes.</exception>
    public static void ToCurrent(BudgetData data)
    {
        if (data.Version is < 1 or > BudgetData.CurrentVersion) throw new BudgetException("Versão de arquivo não suportada. Atualize o BankManager ou use um backup compatível.");
        // Passo 1 → 2: acrescenta as recorrências. Uma lista vazia equivale a "nenhuma despesa mensal", então nada se perde.
        // A versão 1 não conhecia recorrências: um arquivo que as traga com esse número de versão está adulterado ou corrompido.
        if (data.Version == 1)
        {
            if (data.Recurrences is { Count: > 0 } || data.Expenses?.Any(e => e is not null && (e.RecurrenceId is not null || e.RecurrenceMonth is not null)) == true)
                throw new BudgetException("O arquivo declara a versão 1, mas contém recorrências. Ele pode estar corrompido.");
            data.Recurrences = [];
            data.Version = 2;
        }
    }
}
