namespace BankManager.Nucleo;

/// <summary>
/// Valida a integridade do orçamento inteiro, tanto na entrada de backups quanto antes de qualquer gravação.
/// </summary>
public static class RegrasDoOrcamento
{
    /// <summary>
    /// Vocabulário fechado usado no formulário, nos filtros e nas previsões.
    /// </summary>
    public static readonly string[] Categorias = ["Moradia", "Mercado", "Transporte", "Saúde", "Educação", "Lazer", "Serviços", "Outros"];
    /// <summary>
    /// Categorias disponíveis: as padrão seguidas das criadas pelo usuário.
    /// </summary>
    public static List<string> TodasAsCategorias(DadosDoOrcamento dados) => [.. Categorias, .. dados.CategoriasProprias];
    /// <summary>
    /// Verifica versão, limites, unicidade, referências e coerência financeira, lançando ErroDeOrcamento na primeira falha.
    /// </summary>
    /// <remarks>Não corrige registros nem altera o orçamento recebido. Campos são verificados mesmo que a interface já os tenha validado.</remarks>
    public static void Validar(DadosDoOrcamento dados)
    {
        if (dados.Versao != DadosDoOrcamento.VersaoAtual) throw new ErroDeOrcamento("Versão de arquivo não suportada.");
        if (dados.Pessoas is null || dados.Rendas is null || dados.Despesas is null || dados.Recorrencias is null || dados.Recorrencias.Count > 1_000 || dados.Acertos is null || dados.Acertos.Count > 100_000 || dados.CategoriasProprias is null || dados.Metas is null || dados.Pessoas.Count > 200 || dados.Rendas.Count > 100_000 || dados.Despesas.Count > 100_000)
            throw new ErroDeOrcamento("Arquivo inválido ou com registros demais.");
        // Conjuntos detectam repetições e permitem conferir referências de rendas e despesas às pessoas.
        var ids = new HashSet<Guid>();
        var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in dados.Pessoas)
        {
            if (p is null || p.Id == Guid.Empty || !ids.Add(p.Id)) throw new ErroDeOrcamento("Identificador de pessoa inválido ou repetido.");
            Texto(p.Nome, "Nome", 80);
            if (!nomes.Add(p.Nome.Trim())) throw new ErroDeOrcamento("Já existe uma pessoa com esse nome.");
        }
        // Uma pessoa pode ter várias rendas no mês (salário, extra, 13º), desde que com nomes diferentes e em número razoável.
        var idsDeRenda = new HashSet<Guid>(); var nomesDeRenda = new HashSet<(Guid, DateOnly, string)>(); var porMes = new Dictionary<(Guid, DateOnly), int>();
        foreach (var renda in dados.Rendas)
        {
            if (renda is null || !ids.Contains(renda.IdDaPessoa) || renda.Mes.Day != 1 || renda.Id == Guid.Empty || !idsDeRenda.Add(renda.Id))
                throw new ErroDeOrcamento("Registro de renda inválido ou repetido.");
            Data(renda.Mes); Texto(renda.Rotulo, "Nome da renda", 40);
            if (!nomesDeRenda.Add((renda.IdDaPessoa, renda.Mes, renda.Rotulo.Trim().ToUpperInvariant()))) throw new ErroDeOrcamento("Há duas rendas com o mesmo nome no mesmo mês. Use nomes diferentes, como Salário e Extra.");
            var chave = (renda.IdDaPessoa, renda.Mes); porMes[chave] = porMes.GetValueOrDefault(chave) + 1;
            if (porMes[chave] > 20) throw new ErroDeOrcamento("Cada pessoa pode ter até 20 rendas por mês.");
            Dinheiro.Validar(renda.Valor, "Renda");
        }
        // Categorias próprias: até 40, nomes de 1 a 30 caracteres sem espaços nas pontas e sem repetir (ignorando maiúsculas) as padrão nem entre si.
        if (dados.CategoriasProprias.Count > MotorDeCategorias.MaximoDeProprias) throw new ErroDeOrcamento($"Use no máximo {MotorDeCategorias.MaximoDeProprias} categorias próprias.");
        var nomesDeCategorias = new HashSet<string>(Categorias, StringComparer.OrdinalIgnoreCase);
        foreach (var nome in dados.CategoriasProprias)
        {
            Texto(nome, "Categoria", MotorDeCategorias.TamanhoMaximo);
            if (nome != nome.Trim() || !nomesDeCategorias.Add(nome)) throw new ErroDeOrcamento("Categoria própria inválida ou com nome repetido.");
        }
        // Metas: uma por categoria existente, com limite positivo.
        var categoriasComMeta = new HashSet<string>();
        foreach (var meta in dados.Metas)
        {
            if (meta is null || !nomesDeCategorias.Contains(meta.Categoria) || !categoriasComMeta.Add(meta.Categoria.ToUpperInvariant())) throw new ErroDeOrcamento("Meta inválida: use uma categoria existente, uma meta por categoria.");
            Dinheiro.Validar(meta.Valor, "Meta", true);
        }
        // Um acerto liga duas pessoas diferentes que existem, com valor positivo.
        var idsDeAcertos = new HashSet<Guid>();
        foreach (var s in dados.Acertos)
        {
            if (s is null || s.Id == Guid.Empty || !idsDeAcertos.Add(s.Id) || !ids.Contains(s.IdDoPagador) || !ids.Contains(s.IdDoRecebedor) || s.IdDoPagador == s.IdDoRecebedor)
                throw new ErroDeOrcamento("Acerto de contas inválido: escolha duas pessoas diferentes.");
            Dinheiro.Validar(s.Valor, "Valor do acerto", true); Data(s.Data);
        }
        // Modelos de recorrência vêm antes das despesas, que podem referenciá-los.
        var idsDeRecorrencias = new HashSet<Guid>();
        foreach (var r in dados.Recorrencias)
        {
            if (r is null || r.Id == Guid.Empty || !idsDeRecorrencias.Add(r.Id)) throw new ErroDeOrcamento("Identificador de recorrência inválido ou repetido.");
            Texto(r.Titulo, "Descrição", 120);
            if (!nomesDeCategorias.Contains(r.Categoria)) throw new ErroDeOrcamento("Categoria inválida.");
            Dinheiro.Validar(r.Valor, "Valor da recorrência", true);
            if (r.DiaDoMes is < 1 or > 31) throw new ErroDeOrcamento("Dia do vencimento da recorrência inválido: use de 1 a 31.");
            ValidarDivisao(r.Divisao, r.Participantes, ids);
            if (r.MesInicial.Day != 1 || r.MesFinal is { Day: not 1 } || r.MesFinal is { } fim && fim < r.MesInicial) throw new ErroDeOrcamento("Período da recorrência inválido.");
            Data(r.MesInicial); if (r.MesFinal is { } ultimo) Data(ultimo);
            if (r.MesesPulados is null || r.MesesPulados.Count > 1_200 || r.MesesPulados.Any(m => m.Day != 1) || r.MesesPulados.Distinct().Count() != r.MesesPulados.Count) throw new ErroDeOrcamento("Meses ignorados da recorrência inválidos.");
        }
        // Cada lançamento gerado por uma recorrência ocupa um mês exclusivo dela.
        var ocorrencias = new HashSet<(Guid, DateOnly)>();
        // Cada lançamento é validado individualmente; parcelas podem ser excluídas ou editadas separadamente.
        var idsDasDespesas = new HashSet<Guid>();
        foreach (var e in dados.Despesas)
        {
            if (e is null || e.Id == Guid.Empty || !idsDasDespesas.Add(e.Id)) throw new ErroDeOrcamento("Identificador de despesa inválido ou repetido.");
            Texto(e.Titulo, "Descrição", 120);
            if (!nomesDeCategorias.Contains(e.Categoria)) throw new ErroDeOrcamento("Categoria inválida.");
            Data(e.Vencimento);
            Dinheiro.Validar(e.Valor, "Valor da despesa", true);
            // Referências inexistentes ou duplicadas causariam rateios ambíguos; o modo também precisa ser conhecido.
            ValidarDivisao(e.Divisao, e.Participantes, ids, "Selecione as pessoas responsáveis pela despesa.");
            if (e.TotalDeParcelas is < 1 or > 120 || e.NumeroDaParcela < 1 || e.NumeroDaParcela > e.TotalDeParcelas || (e.TotalDeParcelas > 1 && (e.GrupoDeParcelas is null || e.GrupoDeParcelas == Guid.Empty)))
                throw new ErroDeOrcamento("Parcelamento inválido. Use de 1 a 120 parcelas.");
            // Revalida quantidade, preço e soma: o total recebido de um backup não é considerado confiável.
            if (e.Itens is null || e.Itens.Count > 500) throw new ErroDeOrcamento("A lista permite até 500 itens.");
            foreach (var item in e.Itens)
            {
                if (item is null) throw new ErroDeOrcamento("Item inválido.");
                Texto(item.Nome, "Nome do item", 100);
                if (item.Quantidade <= 0 || item.Quantidade > 100_000 || decimal.Round(item.Quantidade, 3) != item.Quantidade) throw new ErroDeOrcamento("Quantidade deve estar entre 0,001 e 100.000, com até três casas decimais.");
                Dinheiro.Validar(item.PrecoUnitario, "Preço unitário", true);
                Dinheiro.Validar(item.Total, "Total do item", true);
            }
            if (e.Itens.Count > 0 && (e.TotalDeParcelas > 1 || e.Itens.Sum(i => i.Total) != e.Valor))
                throw new ErroDeOrcamento("O total da despesa precisa ser igual à soma dos itens. Listas devem ser salvas à vista.");
            // Quem pagou só existe em despesas pagas e precisa ser uma pessoa cadastrada.
            if (e.PagaPor is { } pagador && (!e.Paga || !ids.Contains(pagador))) throw new ErroDeOrcamento("Quem pagou: escolha uma pessoa cadastrada em uma despesa já paga.");
            // A data de pagamento só existe em despesas pagas e fica no mesmo intervalo de datas aceito pelo aplicativo.
            if (e.DataDoPagamento is { } pagaEm) { if (!e.Paga) throw new ErroDeOrcamento("Data do pagamento: só pode ser informada em uma despesa já paga."); Data(pagaEm); }
            if (e.IdDeOrigem == Guid.Empty) throw new ErroDeOrcamento("Origem da despesa importada inválida.");
            // Vínculo com recorrência: id e mês andam juntos, o modelo existe, o mês é exclusivo e o lançamento é simples.
            if ((e.IdDaRecorrencia is null) != (e.MesDaRecorrencia is null)) throw new ErroDeOrcamento("Vínculo com a recorrência incompleto.");
            if (e.IdDaRecorrencia is { } recorrencia && (!idsDeRecorrencias.Contains(recorrencia) || e.MesDaRecorrencia!.Value.Day != 1 || !ocorrencias.Add((recorrencia, e.MesDaRecorrencia.Value)) || e.TotalDeParcelas > 1 || e.Itens.Count > 0))
                throw new ErroDeOrcamento("Despesa recorrente inválida: não pode ter parcelas nem lista de itens, e cada mês aparece uma única vez.");
        }
    }
    /// <summary>
    /// Confere modo de divisão e responsáveis (existentes, sem repetição, um só no modo individual), nas despesas e nas recorrências.
    /// </summary>
    private static void ValidarDivisao(ModoDeDivisao divisao, List<Guid>? participantes, HashSet<Guid> pessoas, string mensagem = "Selecione as pessoas responsáveis pela recorrência.")
    {
        if (!Enum.IsDefined(divisao) || participantes is null || participantes.Count == 0 || participantes.Count != participantes.Distinct().Count() || participantes.Any(id => !pessoas.Contains(id))) throw new ErroDeOrcamento(mensagem);
        if (divisao == ModoDeDivisao.Individual && participantes.Count != 1) throw new ErroDeOrcamento("Uma despesa individual deve ter exatamente uma pessoa.");
    }
    /// <summary>
    /// Rejeita texto vazio, acima do limite ou com caracteres de controle, como quebras de linha.
    /// </summary>
    public static void Texto(string? texto, string rotulo, int limiteMaximo)
    {
        if (string.IsNullOrWhiteSpace(texto) || texto.Length > limiteMaximo || texto.Any(char.IsControl))
            throw new ErroDeOrcamento($"{rotulo}: preencha entre 1 e {limiteMaximo} caracteres, sem caracteres de controle.");
    }
    /// <summary>
    /// Restringe os registros ao intervalo de anos suportado pelo domínio, de 2000 a 2100.
    /// </summary>
    public static void Data(DateOnly data)
    {
        if (data.Year is < 2000 or > 2100) throw new ErroDeOrcamento("Use datas entre os anos 2000 e 2100.");
    }
    /// <summary>
    /// Distribui o valor total de uma compra entre vencimentos mensais, sem perder ou criar centavos.
    /// </summary>
    /// <param name="original">Modelo da compra com valor total em reais, já validado como dinheiro com duas casas.</param>
    /// <param name="quantidade">Número de parcelas, entre 1 e 120.</param>
    /// <returns>Novos lançamentos com identificadores próprios, sem modificar o modelo recebido.</returns>
    public static List<Despesa> Parcelar(Despesa original, int quantidade)
    {
        if (quantidade is < 1 or > 120) throw new ErroDeOrcamento("Use de 1 a 120 parcelas.");
        if (original.Itens.Count > 0 && quantidade > 1) throw new ErroDeOrcamento("Listas de itens devem ser salvas à vista.");
        // Trabalhar em centavos inteiros permite distribuir o resto pelas primeiras parcelas.
        var centavos = decimal.ToInt64(original.Valor * 100);
        if (centavos < quantidade) throw new ErroDeOrcamento("Cada parcela precisa ter pelo menos R$ 0,01.");
        var grupo = quantidade > 1 ? Guid.NewGuid() : (Guid?)null;
        // AddMonths parte sempre da data original: 31/jan → último dia de fevereiro → 31/mar.
        // O grupo relaciona parcelas; as listas são copiadas para não compartilhar suas coleções mutáveis.
        return Enumerable.Range(0, quantidade).Select(i => original with
        {
            Id = Guid.NewGuid(), Valor = (centavos / quantidade + (i < centavos % quantidade ? 1 : 0)) / 100m,
            Vencimento = original.Vencimento.AddMonths(i), GrupoDeParcelas = grupo,
            NumeroDaParcela = i + 1, TotalDeParcelas = quantidade,
            Paga = quantidade == 1 && original.Paga, PagaPor = quantidade == 1 ? original.PagaPor : null, DataDoPagamento = quantidade == 1 ? original.DataDoPagamento : null, Participantes = [..original.Participantes], Itens = [..original.Itens]
        }).ToList();
    }
}
