namespace BankManager.Nucleo;

/// <summary>
/// Resultado do rateio: valor por identificador de pessoa e aviso quando foi necessário usar divisão igual.
/// </summary>
/// <param name="Valores">Valores em reais cuja soma corresponde ao lançamento.</param>
/// <param name="Aviso">Explicação do fallback por falta de renda disponível; nulo quando não houve fallback.</param>
public sealed record Rateio(Dictionary<Guid, decimal> Valores, string? Aviso = null);
/// <summary>
/// Resumo mensal de uma pessoa, calculado a partir das despesas e da renda da competência.
/// </summary>
/// <param name="Pessoa">Pessoa à qual o resumo pertence.</param>
/// <param name="Renda">Renda líquida do mês.</param>
/// <param name="Atribuido">Total de responsabilidades, pagas ou em aberto.</param>
/// <param name="Pago">Responsabilidade em lançamentos marcados como integralmente pagos.</param>
public sealed record SaldoDaPessoa(Pessoa Pessoa, decimal Renda, decimal Atribuido, decimal Pago)
{
    /// <summary>
    /// Renda menos todas as responsabilidades do mês; pode ser negativa e não equivale a saldo bancário.
    /// </summary>
    public decimal Sobra => Renda - Atribuido;
}
/// <summary>
/// Estimativa de uma categoria, com histórico e compromissos separados para explicar o cálculo.
/// </summary>
/// <param name="Categoria">Categoria analisada.</param>
/// <param name="Historico">Média mensal das despesas não parceladas na janela observada.</param>
/// <param name="Agendado">Total de lançamentos, à vista e parcelados, já cadastrados no mês alvo.</param>
/// <param name="Estimativa">Maior valor entre média e lançamentos à vista, acrescido das parcelas do mês alvo.</param>
/// <param name="Meses">Tamanho da janela histórica em meses, entre zero e três, incluindo meses sem lançamentos.</param>
public sealed record Previsao(string Categoria, decimal Historico, decimal Agendado, decimal Estimativa, int Meses);
/// <summary>
/// Total de despesas de um mês, usado pelo gráfico da visão geral.
/// </summary>
/// <param name="Mes">Primeiro dia do mês.</param>
/// <param name="Total">Soma de todas as despesas com vencimento no mês, pagas ou em aberto; zero quando não há nenhuma.</param>
public sealed record TotalDoMes(DateOnly Mes, decimal Total);
/// <summary>
/// Uma entrada ou saída de dinheiro mostrada em "Últimas movimentações".
/// </summary>
/// <param name="Data">Data da movimentação: o dia do pagamento da despesa (ou o vencimento, se o pagamento não tem data) ou o dia 1 do mês para rendas (a renda só tem mês).</param>
/// <param name="Titulo">Descrição da despesa ou nome da linha de renda (por exemplo "Salário").</param>
/// <param name="Categoria">Categoria da despesa ou, nas rendas, "Renda de" seguido do nome da pessoa.</param>
/// <param name="Valor">Valor sempre positivo; <paramref name="Entrada"/> indica o sentido.</param>
/// <param name="Entrada">Verdadeiro para entradas (rendas), falso para saídas (despesas pagas).</param>
public sealed record Movimentacao(DateOnly Data, string Titulo, string Categoria, decimal Valor, bool Entrada);
/// <summary>
/// Consultas e cálculos financeiros sem acesso a disco ou controles da interface. Não modifica os dados recebidos.
/// </summary>
public static class MotorFinanceiro
{
    /// <summary>
    /// Consulta a renda da pessoa na competência (soma de todas as suas linhas); ausência de registro é tratada como zero, sem copiar outro mês.
    /// </summary>
    public static decimal Renda(DadosDoOrcamento dados, Guid pessoa, DateOnly mes) => LinhasDeRenda(dados, pessoa, mes).Sum(i => i.Valor);
    /// <summary>
    /// Linhas de renda da pessoa na competência, na ordem em que foram cadastradas.
    /// </summary>
    public static List<RendaMensal> LinhasDeRenda(DadosDoOrcamento dados, Guid pessoa, DateOnly mes) => dados.Rendas.Where(i => i.IdDaPessoa == pessoa && i.Mes == Datas.Mes(mes)).ToList();
    /// <summary>
    /// Obtém lançamentos do mês selecionado, ordenados por vencimento e depois por descrição.
    /// </summary>
    public static List<Despesa> Despesas(DadosDoOrcamento dados, DateOnly mes) => dados.Despesas.Where(e => Datas.Mes(e.Vencimento) == Datas.Mes(mes)).OrderBy(e => e.Vencimento).ThenBy(e => e.Titulo).ToList();
    /// <summary>
    /// Rateia uma despesa validada pelos responsáveis e distribui os centavos pelos maiores restos fracionários.
    /// </summary>
    /// <remarks>Despesas individuais, inclusive pagas, reduzem os pesos proporcionais. Se todos os pesos forem zero, aplica divisão igual com aviso.</remarks>
    public static Rateio Ratear(DadosDoOrcamento dados, Despesa despesa)
    {
        var pessoas = despesa.Participantes;
        if (pessoas.Count == 0) throw new ErroDeOrcamento("Nenhum responsável selecionado.");
        // Pesos iguais atendem ao modo igual e ao individual (que possui apenas um responsável).
        var pesos = pessoas.ToDictionary(id => id, _ => 1m);
        string? alerta = null;
        if (despesa.Divisao == ModoDeDivisao.RendaDisponivel)
        {
            // Calcula uma base fixa para todo o mês: despesas compartilhadas não são descontadas recursivamente.
            var mensais = Despesas(dados, despesa.Vencimento);
            foreach (var id in pessoas)
                pesos[id] = Math.Max(0, Renda(dados, id, despesa.Vencimento) - mensais.Where(e => e.Divisao == ModoDeDivisao.Individual && e.Participantes.Contains(id)).Sum(e => e.Valor));
            // Evita divisão por zero e torna explícita a decisão quando todos estão sem renda disponível.
            if (pesos.Values.Sum() == 0)
            {
                alerta = "Sem renda disponível entre os participantes: divisão igual aplicada. Confira as rendas e despesas individuais.";
                pesos = pessoas.ToDictionary(id => id, _ => 1m);
            }
        }
        var pesoTotal = pesos.Values.Sum();
        var centavos = decimal.ToInt64(despesa.Valor * 100);
        // Primeiro arredonda cada cota para baixo em centavos; a soma pode deixar alguns centavos sem destino.
        var bruto = pessoas.Select(id => (Id: id, Valor: centavos * pesos[id] / pesoTotal)).ToList();
        var resultado = bruto.ToDictionary(p => p.Id, p => decimal.Floor(p.Valor) / 100m);
        var restoDaDivisao = (int)(centavos - resultado.Values.Sum() * 100);
        // Os maiores restos recebem um centavo extra. O Guid desempata de forma estável dentro da mesma base.
        foreach (var p in bruto.OrderByDescending(p => p.Valor - decimal.Floor(p.Valor)).ThenBy(p => p.Id).Take(restoDaDivisao)) resultado[p.Id] += .01m;
        return new Rateio(resultado, alerta);
    }
    /// <summary>
    /// Soma as cotas de cada pessoa no mês e separa o que está quitado, sem descontar o pago duas vezes.
    /// </summary>
    public static List<SaldoDaPessoa> Saldos(DadosDoOrcamento dados, DateOnly mes)
    {
        var rateios = Despesas(dados, mes).Select(e => (Despesa: e, Rateio: Ratear(dados, e))).ToList();
        return dados.Pessoas.Select(p => new SaldoDaPessoa(p, Renda(dados, p.Id, mes),
            rateios.Sum(a => a.Rateio.Valores.GetValueOrDefault(p.Id)),
            rateios.Where(a => a.Despesa.Paga).Sum(a => a.Rateio.Valores.GetValueOrDefault(p.Id)))).ToList();
    }
    /// <summary>
    /// Estima gastos por categoria usando até três meses anteriores ao alvo e os compromissos já cadastrados.
    /// </summary>
    /// <remarks>A janela não inclui o mês alvo. Meses vazios dentro dela contam como zero; um mês anterior ainda em andamento pode conter histórico parcial.</remarks>
    public static List<Previsao> Previsoes(DadosDoOrcamento dados, DateOnly destino)
    {
        destino = Datas.Mes(destino);
        // A janela começa no primeiro mês com compra não parcelada, limitada aos três meses mais recentes.
        var primeiro = dados.Despesas.Where(e => e.Vencimento < destino && e.TotalDeParcelas == 1).Select(e => Datas.Mes(e.Vencimento)).DefaultIfEmpty(destino).Min();
        var inicio = primeiro > destino.AddMonths(-3) ? primeiro : destino.AddMonths(-3);
        int meses = (destino.Year - inicio.Year) * 12 + destino.Month - inicio.Month;
        return RegrasDoOrcamento.TodasAsCategorias(dados).Select(categoria =>
        {
            // Parcelas não entram na média para que uma dívida encerrada não seja projetada indefinidamente.
            var historico = dados.Despesas.Where(e => e.Categoria == categoria && e.TotalDeParcelas == 1 && e.Vencimento >= inicio && e.Vencimento < destino).Sum(e => e.Valor);
            var media = meses == 0 ? 0 : decimal.Round(historico / meses, 2, MidpointRounding.AwayFromZero);
            var agendadas = Despesas(dados, destino).Where(e => e.Categoria == categoria).ToList();
            var parcelas = agendadas.Where(e => e.TotalDeParcelas > 1).Sum(e => e.Valor);
            var outra = agendadas.Where(e => e.TotalDeParcelas == 1).Sum(e => e.Valor);
            // O máximo evita somar novamente uma compra à vista já prevista na média. Parcelas são compromissos adicionais.
            return new Previsao(categoria, media, parcelas + outra, parcelas + Math.Max(media, outra), meses);
        }).Where(f => f.Estimativa > 0 || f.Historico > 0).ToList();
    }
    /// <summary>
    /// Totais de despesas dos últimos <paramref name="quantidade"/> meses, terminando no mês informado (inclusive), do mais antigo ao mais recente.
    /// </summary>
    /// <remarks>Meses sem despesas aparecem com total zero, para o gráfico manter sempre o mesmo número de barras.</remarks>
    public static List<TotalDoMes> TotaisMensais(DadosDoOrcamento dados, DateOnly mes, int quantidade)
    {
        if (quantidade is < 1 or > 120) throw new ArgumentOutOfRangeException(nameof(quantidade));
        var ultimo = Datas.Mes(mes);
        var totais = dados.Despesas.GroupBy(e => Datas.Mes(e.Vencimento)).ToDictionary(g => g.Key, g => g.Sum(e => e.Valor));
        return Enumerable.Range(0, quantidade).Select(i => ultimo.AddMonths(i - quantidade + 1)).Select(m => new TotalDoMes(m, totais.GetValueOrDefault(m))).ToList();
    }
    /// <summary>
    /// Movimentações do mês, das mais recentes para as mais antigas, limitadas a <paramref name="limite"/> itens.
    /// </summary>
    /// <remarks>
    /// Entram as rendas do mês (com data no dia 1; linhas de valor zero ficam de fora) e as despesas já marcadas como pagas.
    /// A data de uma despesa paga é a do pagamento; quando ela não foi informada (despesas pagas em versões antigas), vale o
    /// vencimento. A despesa aparece no mês em que essa data cai, mesmo que o vencimento seja em outro mês.
    /// Em datas iguais a ordem é estável: primeiro as despesas (por vencimento e descrição), depois as rendas (na ordem de cadastro).
    /// </remarks>
    public static List<Movimentacao> MovimentacoesRecentes(DadosDoOrcamento dados, DateOnly mes, int limite = 6)
    {
        if (limite < 1) throw new ArgumentOutOfRangeException(nameof(limite));
        var primeiro = Datas.Mes(mes);
        var paga = dados.Despesas.Where(e => e.Paga && Datas.Mes(e.DataDeReferencia) == primeiro).OrderBy(e => e.Vencimento).ThenBy(e => e.Titulo)
            .Select(e => new Movimentacao(e.DataDeReferencia, e.Titulo, e.Categoria, e.Valor, false));
        var rendas = dados.Rendas.Where(i => i.Mes == primeiro && i.Valor > 0)
            .Select(i => new Movimentacao(primeiro, i.Rotulo, "Renda de " + (dados.Pessoas.FirstOrDefault(p => p.Id == i.IdDaPessoa)?.Nome ?? "—"), i.Valor, true));
        // OrderByDescending é estável: itens da mesma data mantêm a ordem em que foram concatenados.
        return paga.Concat(rendas).OrderByDescending(m => m.Data).Take(limite).ToList();
    }
}
