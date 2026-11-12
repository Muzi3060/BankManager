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
            if (data.Recurrences is { Count: > 0 } || data.Settlements is { Count: > 0 } || data.CustomCategories is { Count: > 0 } || data.Goals is { Count: > 0 } || data.Expenses?.Any(e => e is not null && (e.RecurrenceId is not null || e.RecurrenceMonth is not null || e.PaidBy is not null)) == true)
                throw new BudgetException("O arquivo declara a versão 1, mas contém dados que só existem a partir da versão 2 (recorrências, quem pagou ou acertos). Ele pode estar corrompido.");
            data.Recurrences = []; data.Settlements = []; data.CustomCategories = []; data.Goals = [];
            data.Version = 2;
        }
        // Passo 2 → 3: acrescenta a data de pagamento e a origem de despesas importadas, ambos opcionais. Despesas já pagas
        // ficam sem data de pagamento (ninguém a informou); a versão 2 não conhecia esses campos, então um arquivo v2 que os traga é recusado.
        if (data.Version == 2)
        {
            if (data.Expenses?.Any(e => e is not null && (e.PaidDate is not null || e.OriginId is not null)) == true)
                throw new BudgetException("O arquivo declara a versão 2, mas contém dados que só existem a partir da versão 3 (data de pagamento ou origem de importação). Ele pode estar corrompido.");
            data.Version = 3;
        }
    }
}
