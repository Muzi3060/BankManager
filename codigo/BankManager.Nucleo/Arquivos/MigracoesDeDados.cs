namespace BankManager.Nucleo;

/// <summary>
/// Atualiza dados lidos de arquivos antigos para o esquema atual, sem perder nada. Cada versão tem um passo próprio,
/// executado em ordem, de modo que um arquivo muito antigo passa por todos os passos até chegar à versão atual.
/// </summary>
public static class MigracoesDeDados
{
    /// <summary>
    /// Leva o orçamento à <see cref="DadosDoOrcamento.VersaoAtual"/>. Versões desconhecidas (inclusive futuras) são recusadas.
    /// </summary>
    /// <exception cref="ErroDeOrcamento">A versão não é suportada ou o arquivo mistura campos de versões diferentes.</exception>
    public static void ParaVersaoAtual(DadosDoOrcamento dados)
    {
        if (dados.Versao is < 1 or > DadosDoOrcamento.VersaoAtual) throw new ErroDeOrcamento("Versão de arquivo não suportada. Atualize o BankManager ou use um backup compatível.");
        // Passo 1 → 2: acrescenta as recorrências. Uma lista vazia equivale a "nenhuma despesa mensal", então nada se perde.
        // A versão 1 não conhecia recorrências: um arquivo que as traga com esse número de versão está adulterado ou corrompido.
        if (dados.Versao == 1)
        {
            if (dados.Recorrencias is { Count: > 0 } || dados.Acertos is { Count: > 0 } || dados.CategoriasProprias is { Count: > 0 } || dados.Metas is { Count: > 0 } || dados.Despesas?.Any(e => e is not null && (e.IdDaRecorrencia is not null || e.MesDaRecorrencia is not null || e.PagaPor is not null)) == true)
                throw new ErroDeOrcamento("O arquivo declara a versão 1, mas contém dados que só existem a partir da versão 2 (recorrências, quem pagou ou acertos). Ele pode estar corrompido.");
            dados.Recorrencias = []; dados.Acertos = []; dados.CategoriasProprias = []; dados.Metas = [];
            dados.Versao = 2;
        }
        // Passo 2 → 3: acrescenta a data de pagamento e a origem de despesas importadas, ambos opcionais. Despesas já pagas
        // ficam sem data de pagamento (ninguém a informou); a versão 2 não conhecia esses campos, então um arquivo v2 que os traga é recusado.
        if (dados.Versao == 2)
        {
            if (dados.Despesas?.Any(e => e is not null && (e.DataDoPagamento is not null || e.IdDeOrigem is not null)) == true)
                throw new ErroDeOrcamento("O arquivo declara a versão 2, mas contém dados que só existem a partir da versão 3 (data de pagamento ou origem de importação). Ele pode estar corrompido.");
            dados.Versao = 3;
        }
    }
}
