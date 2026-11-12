namespace BankManager.Nucleo;

/// <summary>
/// Gera, encerra e remove despesas recorrentes. Trabalha sobre os dados recebidos (a interface passa uma cópia e só
/// adota o resultado depois de salvar) e não acessa disco nem controles.
/// </summary>
public static class MotorDeRecorrencias
{
    /// <summary>Quantos meses à frente do mês atual os lançamentos recorrentes são criados.</summary>
    public const int MesesDeHorizonte = 12;
    /// <summary>
    /// Último mês criado automaticamente: o mês de <paramref name="hoje"/> mais <see cref="MesesDeHorizonte"/>, limitado a dezembro de 2099
    /// (o limite da navegação do aplicativo).
    /// </summary>
    public static DateOnly Horizonte(DateOnly hoje)
    {
        var horizonte = Datas.Mes(hoje).AddMonths(MesesDeHorizonte);
        return horizonte.Year > 2099 ? new DateOnly(2099, 12, 1) : horizonte;
    }
    /// <summary>
    /// Cria os lançamentos que faltam, de cada recorrência, do mês inicial até o horizonte (ou o mês final, se houver).
    /// Pode ser chamado quantas vezes quiser: só cria o que não existe e respeita os meses excluídos de propósito.
    /// O mês inicial é sempre criado, mesmo que esteja além do horizonte.
    /// </summary>
    /// <returns>Quantidade de lançamentos criados.</returns>
    public static int Gerar(DadosDoOrcamento dados, DateOnly hoje)
    {
        var horizonte = Horizonte(hoje); var criadas = 0;
        foreach (var recorrencia in dados.Recorrencias)
        {
            var ultimo = recorrencia.MesInicial > horizonte ? recorrencia.MesInicial : horizonte;
            if (recorrencia.MesFinal is { } fim && fim < ultimo) ultimo = fim;
            var existente = dados.Despesas.Where(e => e.IdDaRecorrencia == recorrencia.Id).Select(e => e.MesDaRecorrencia).ToHashSet();
            for (var mes = recorrencia.MesInicial; mes <= ultimo; mes = mes.AddMonths(1))
            {
                if (existente.Contains(mes) || recorrencia.MesesPulados.Contains(mes)) continue;
                // Meses curtos usam o último dia: dia 31 em fevereiro vence em 28 ou 29.
                var dia = Math.Min(recorrencia.DiaDoMes, DateTime.DaysInMonth(mes.Year, mes.Month));
                dados.Despesas.Add(new Despesa
                {
                    Titulo = recorrencia.Titulo, Categoria = recorrencia.Categoria, Valor = recorrencia.Valor, Vencimento = new DateOnly(mes.Year, mes.Month, dia),
                    Divisao = recorrencia.Divisao, Participantes = [.. recorrencia.Participantes], IdDaRecorrencia = recorrencia.Id, MesDaRecorrencia = mes
                });
                criadas++;
            }
        }
        return criadas;
    }
    /// <summary>
    /// Encerra a recorrência: <paramref name="ultimoMes"/> passa a ser o último mês gerado. Os lançamentos em aberto dos meses
    /// seguintes são removidos; os já pagos ficam no histórico.
    /// </summary>
    /// <exception cref="ErroDeOrcamento">A recorrência não existe, já foi encerrada ou o mês é anterior ao início.</exception>
    public static void Encerrar(DadosDoOrcamento dados, Guid idDaRecorrencia, DateOnly ultimoMes)
    {
        var recorrencia = dados.Recorrencias.FirstOrDefault(r => r.Id == idDaRecorrencia) ?? throw new ErroDeOrcamento("Recorrência não encontrada.");
        if (recorrencia.MesFinal is not null) throw new ErroDeOrcamento("Esta recorrência já foi encerrada.");
        ultimoMes = Datas.Mes(ultimoMes);
        if (ultimoMes < recorrencia.MesInicial) throw new ErroDeOrcamento("O último mês não pode ser anterior ao início da recorrência. Escolha um mês a partir de " + recorrencia.MesInicial.ToString("MM/yyyy") + ".");
        recorrencia.MesFinal = ultimoMes;
        dados.Despesas.RemoveAll(e => e.IdDaRecorrencia == idDaRecorrencia && !e.Paga && e.MesDaRecorrencia > ultimoMes);
    }
    /// <summary>
    /// Remove só o modelo, mantendo os lançamentos já criados como despesas comuns (nada do histórico é apagado).
    /// </summary>
    public static void Remover(DadosDoOrcamento dados, Guid idDaRecorrencia)
    {
        if (dados.Recorrencias.RemoveAll(r => r.Id == idDaRecorrencia) == 0) throw new ErroDeOrcamento("Recorrência não encontrada.");
        foreach (var despesa in dados.Despesas.Where(e => e.IdDaRecorrencia == idDaRecorrencia)) { despesa.IdDaRecorrencia = null; despesa.MesDaRecorrencia = null; }
    }
    /// <summary>
    /// Exclui um lançamento. Se ele veio de uma recorrência, registra o mês como ignorado para que a geração automática não o recrie.
    /// </summary>
    public static void RemoverDespesa(DadosDoOrcamento dados, Guid idDaDespesa)
    {
        var despesa = dados.Despesas.FirstOrDefault(e => e.Id == idDaDespesa); if (despesa is null) return;
        dados.Despesas.Remove(despesa);
        if (despesa.IdDaRecorrencia is { } id && despesa.MesDaRecorrencia is { } mes && dados.Recorrencias.FirstOrDefault(r => r.Id == id) is { } recorrencia && !recorrencia.MesesPulados.Contains(mes))
            recorrencia.MesesPulados.Add(mes);
    }
}
