using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BankManager.Nucleo;

/// <summary>
/// Arquivo de compartilhamento de despesas. É outro TIPO de arquivo, diferente do backup: leva só as despesas escolhidas
/// (com as pessoas citadas por nome e as categorias próprias que elas usam) e, ao ser importado, apenas ADICIONA despesas.
/// Nunca contém rendas, metas, acertos nem despesas não selecionadas.
/// </summary>
public sealed record ArquivoDeCompartilhamento
{
    /// <summary>Valor do campo <see cref="Tipo"/> que identifica um compartilhamento.</summary>
    public const string TipoCompartilhamento = "share";
    /// <summary>Versão atual do formato do arquivo de compartilhamento (independente da versão do backup).</summary>
    public const int FormatoAtual = 1;
    /// <summary>Extensão própria, para o arquivo não ser confundido com um backup.</summary>
    public const string Extensao = ".bmshare.json";
    /// <summary>Nome do aplicativo gravado no arquivo.</summary>
    public const string NomeDoAplicativo = "BankManager";
    /// <summary>Tipo do arquivo; sempre "share".</summary>
    [JsonPropertyName("Kind")] public string Tipo { get; set; } = TipoCompartilhamento;
    /// <summary>Aplicativo que gerou o arquivo.</summary>
    [JsonPropertyName("App")] public string Aplicativo { get; set; } = NomeDoAplicativo;
    /// <summary>Versão do formato; um número maior que o conhecido pelo aplicativo é recusado.</summary>
    [JsonPropertyName("FormatVersion")] public int VersaoDoFormato { get; set; } = FormatoAtual;
    /// <summary>Versão do aplicativo que gerou o arquivo (por exemplo "2.2"); só informativa, pode vir vazia.</summary>
    [JsonPropertyName("AppVersion")] public string VersaoDoAplicativo { get; set; } = "";
    /// <summary>Nomes das pessoas citadas pelas despesas (responsáveis e, se incluído, quem pagou).</summary>
    [JsonPropertyName("People")] public List<string> Pessoas { get; set; } = [];
    /// <summary>Categorias próprias usadas pelas despesas do arquivo.</summary>
    [JsonPropertyName("CustomCategories")] public List<string> CategoriasProprias { get; set; } = [];
    /// <summary>Despesas compartilhadas.</summary>
    [JsonPropertyName("Expenses")] public List<DespesaCompartilhada> Despesas { get; set; } = [];
}
/// <summary>
/// Uma despesa dentro do arquivo de compartilhamento. As pessoas aparecem pelo NOME, porque os identificadores de quem
/// envia não existem na base de quem recebe.
/// </summary>
public sealed record DespesaCompartilhada
{
    /// <summary>Identificador estável de origem: o mesmo em toda exportação da mesma despesa, usado para não duplicá-la.</summary>
    [JsonPropertyName("OriginId")] public Guid IdDeOrigem { get; set; }
    [JsonPropertyName("Title")] public string Titulo { get; set; } = "";
    [JsonPropertyName("Category")] public string Categoria { get; set; } = "Outros";
    [JsonPropertyName("DueDate")] public DateOnly Vencimento { get; set; }
    [JsonPropertyName("Amount")] public decimal Valor { get; set; }
    [JsonPropertyName("Items")] public List<ItemDaDespesa> Itens { get; set; } = [];
    /// <summary>Grupo das parcelas na base de origem; na importação vira um grupo novo, igual para as parcelas do mesmo grupo.</summary>
    [JsonPropertyName("InstallmentGroup")] public Guid? GrupoDeParcelas { get; set; }
    [JsonPropertyName("InstallmentNumber")] public int NumeroDaParcela { get; set; } = 1;
    [JsonPropertyName("InstallmentCount")] public int TotalDeParcelas { get; set; } = 1;
    [JsonPropertyName("Split")] public ModoDeDivisao Divisao { get; set; }
    /// <summary>Nomes dos responsáveis.</summary>
    [JsonPropertyName("Participants")] public List<string> Participantes { get; set; } = [];
    /// <summary>Situação de pagamento; falsa quando quem exportou escolheu não incluí-la.</summary>
    [JsonPropertyName("Paid")] public bool Paga { get; set; }
    [JsonPropertyName("PaidDate")] public DateOnly? DataDoPagamento { get; set; }
    /// <summary>Nome de quem pagou, se incluído.</summary>
    [JsonPropertyName("PaidBy")] public string? PagaPor { get; set; }
}
/// <summary>
/// O que fazer com uma pessoa citada no arquivo.
/// </summary>
public enum AcaoDaPessoa
{
    /// <summary>Usar uma pessoa já cadastrada.</summary>
    Existente,
    /// <summary>Criar um cadastro novo com o nome do arquivo.</summary>
    Criar,
    /// <summary>Não trazer a pessoa: ela sai das despesas; as que ficarem sem responsável vão para a pessoa escolhida.</summary>
    Ignorar
}
/// <summary>
/// Escolha para uma pessoa do arquivo; <paramref name="IdDaPessoa"/> só é usado em <see cref="AcaoDaPessoa.Existente"/>.
/// </summary>
public sealed record EscolhaDaPessoa(AcaoDaPessoa Acao, Guid? IdDaPessoa = null);
/// <summary>
/// Decisões de quem importa: como mapear pessoas e categorias, quem assume despesas sem responsável e as opções da importação.
/// </summary>
public sealed record MapeamentoDoCompartilhamento
{
    /// <summary>Uma escolha para cada nome de <see cref="ArquivoDeCompartilhamento.Pessoas"/>.</summary>
    public Dictionary<string, EscolhaDaPessoa> Pessoas { get; init; } = [];
    /// <summary>Para cada categoria própria que não existe aqui: verdadeiro cria a categoria, falso usa "Outros".</summary>
    public Dictionary<string, bool> CriarCategorias { get; init; } = [];
    /// <summary>Pessoa (já cadastrada) que assume as despesas que ficariam sem responsável; obrigatória só se houver alguma.</summary>
    public Guid? ResponsavelPadrao { get; init; }
    /// <summary>Importa as despesas proporcionais à renda como divisão igual.</summary>
    public bool IgualEmVezDeProporcional { get; init; }
    /// <summary>Importa também TODAS as despesas que já existem aqui (como despesas novas, com outro Id).</summary>
    public bool ImportarDuplicadas { get; init; }
    /// <summary>Identificadores de origem das despesas já existentes que devem ser importadas mesmo assim, uma a uma.</summary>
    public IReadOnlySet<Guid> ImportarMesmoAssim { get; init; } = new HashSet<Guid>();
}
/// <summary>
/// O que acontece com uma despesa do arquivo na importação.
/// </summary>
public enum SituacaoDaLinha
{
    /// <summary>Será (ou foi) adicionada.</summary>
    Adicionada,
    /// <summary>Já existe aqui e será pulada.</summary>
    Pulada,
    /// <summary>Tem um erro e não pode ser importada.</summary>
    Erro
}
/// <summary>
/// Uma despesa do arquivo e o seu destino, na ordem do arquivo; alimenta a lista da prévia.
/// </summary>
/// <param name="Origem">A despesa como veio no arquivo.</param>
/// <param name="Situacao">Se será adicionada, pulada ou se tem erro.</param>
/// <param name="JaExiste">Verdadeiro quando a despesa já existe aqui (mesmo que tenha sido importada mesmo assim).</param>
/// <param name="Despesa">A despesa local criada, quando adicionada.</param>
/// <param name="Erro">A explicação do erro, quando houver.</param>
public sealed record LinhaDaImportacao(DespesaCompartilhada Origem, SituacaoDaLinha Situacao, bool JaExiste, Despesa? Despesa = null, string? Erro = null);
/// <summary>
/// Resultado de uma importação (ou da sua prévia).
/// </summary>
/// <param name="Adicionadas">Despesas adicionadas, já com Ids locais.</param>
/// <param name="Duplicadas">Despesas puladas por já existirem aqui (mesmo identificador de origem).</param>
/// <param name="Erros">Uma mensagem por despesa do arquivo que não pôde ser importada.</param>
/// <param name="PessoasNovas">Nomes das pessoas cadastradas pela importação.</param>
/// <param name="CategoriasNovas">Categorias próprias criadas pela importação.</param>
/// <param name="Proporcionais">Quantas despesas adicionadas usam a divisão proporcional à renda (recalculada com as rendas locais).</param>
/// <param name="Linhas">Uma linha por despesa do arquivo, na ordem do arquivo, com o destino de cada uma.</param>
public sealed record ResultadoDaImportacao(List<Despesa> Adicionadas, int Duplicadas, List<string> Erros, List<string> PessoasNovas, List<string> CategoriasNovas, int Proporcionais, List<LinhaDaImportacao> Linhas);
/// <summary>
/// Exporta despesas escolhidas para um arquivo de compartilhamento e importa esse arquivo ADICIONANDO despesas, sem substituir
/// nem remover nada. Trabalha sobre os dados recebidos; só <see cref="Salvar"/> e <see cref="Ler"/> tocam o disco.
/// </summary>
public static class CompartilhamentoDeDespesas
{
    /// <summary>Máximo de despesas em um arquivo de compartilhamento.</summary>
    public const int MaximoDeDespesas = 5_000;
    /// <summary>Tamanho máximo do arquivo de compartilhamento.</summary>
    public const long MaximoDeBytes = 5 * 1024 * 1024;
    /// <summary>
    /// Monta o conteúdo do arquivo com as despesas indicadas. Sem <paramref name="incluirPagamento"/>, todas saem "em aberto",
    /// sem data de pagamento e sem quem pagou. O vínculo com recorrências não é levado: cada despesa vai como despesa comum.
    /// </summary>
    /// <param name="versaoDoAplicativo">Versão do aplicativo, gravada só para informar quem recebe (por exemplo "2.2").</param>
    public static ArquivoDeCompartilhamento Montar(DadosDoOrcamento dados, IReadOnlyCollection<Guid> ids, bool incluirPagamento, string versaoDoAplicativo = "")
    {
        if (ids.Count == 0) throw new ErroDeOrcamento("Selecione pelo menos uma despesa para compartilhar.");
        if (ids.Count > MaximoDeDespesas) throw new ErroDeOrcamento($"Compartilhe até {MaximoDeDespesas} despesas por arquivo.");
        var desejadas = ids.ToHashSet(); var escolhidas = dados.Despesas.Where(e => desejadas.Contains(e.Id)).OrderBy(e => e.Vencimento).ThenBy(e => e.Titulo).ToList();
        if (escolhidas.Count != desejadas.Count) throw new ErroDeOrcamento("Uma das despesas selecionadas não existe mais. Atualize a lista e tente de novo.");
        var nomes = dados.Pessoas.ToDictionary(p => p.Id, p => p.Nome);
        var arquivo = new ArquivoDeCompartilhamento { VersaoDoAplicativo = versaoDoAplicativo };
        foreach (var e in escolhidas)
        {
            var paga = incluirPagamento && e.Paga;
            arquivo.Despesas.Add(new DespesaCompartilhada
            {
                // Uma despesa que já veio de outro arquivo mantém a origem inicial, para ser reconhecida por toda a cadeia.
                IdDeOrigem = e.IdDeOrigem ?? e.Id, Titulo = e.Titulo, Categoria = e.Categoria, Vencimento = e.Vencimento, Valor = e.Valor, Itens = e.Itens.Select(i => i with { }).ToList(),
                GrupoDeParcelas = e.GrupoDeParcelas, NumeroDaParcela = e.NumeroDaParcela, TotalDeParcelas = e.TotalDeParcelas, Divisao = e.Divisao,
                Participantes = e.Participantes.Select(id => nomes[id]).ToList(), Paga = paga, DataDoPagamento = paga ? e.DataDoPagamento : null, PagaPor = paga && e.PagaPor is { } pagador ? nomes[pagador] : null
            });
        }
        // Só as pessoas e as categorias próprias realmente citadas vão para o arquivo.
        var citadas = arquivo.Despesas.SelectMany(e => e.PagaPor is null ? e.Participantes : e.Participantes.Append(e.PagaPor)).ToHashSet();
        arquivo.Pessoas = dados.Pessoas.Select(p => p.Nome).Where(citadas.Contains).ToList();
        arquivo.CategoriasProprias = dados.CategoriasProprias.Where(c => arquivo.Despesas.Any(e => e.Categoria == c)).ToList();
        Validar(arquivo); return arquivo;
    }
    /// <summary>
    /// Confere a estrutura do arquivo: tipo, aplicativo, versão do formato, quantidades e referências. Os valores de cada despesa
    /// (descrição, valor, datas) são conferidos na importação, que aponta o erro por despesa.
    /// </summary>
    public static void Validar(ArquivoDeCompartilhamento arquivo)
    {
        if (arquivo.Tipo != ArquivoDeCompartilhamento.TipoCompartilhamento || arquivo.Aplicativo != ArquivoDeCompartilhamento.NomeDoAplicativo) throw new ErroDeOrcamento("Este arquivo não é um compartilhamento de despesas do BankManager.");
        if (arquivo.VersaoDoFormato > ArquivoDeCompartilhamento.FormatoAtual) throw new ErroDeOrcamento("Este arquivo foi criado por uma versão mais nova do BankManager. Atualize o aplicativo para importá-lo.");
        if (arquivo.VersaoDoFormato < 1) throw new ErroDeOrcamento("Versão do arquivo de compartilhamento inválida.");
        if (arquivo.VersaoDoAplicativo is null || arquivo.VersaoDoAplicativo.Length > 20 || arquivo.VersaoDoAplicativo.Any(c => !char.IsAsciiDigit(c) && c != '.')) throw new ErroDeOrcamento("O arquivo de compartilhamento traz uma versão do aplicativo inválida.");
        if (arquivo.Pessoas is null || arquivo.CategoriasProprias is null || arquivo.Despesas is null || arquivo.Pessoas.Count > 200 || arquivo.CategoriasProprias.Count > MotorDeCategorias.MaximoDeProprias || arquivo.Despesas.Count > MaximoDeDespesas)
            throw new ErroDeOrcamento("Arquivo de compartilhamento inválido ou com registros demais.");
        if (arquivo.Despesas.Count == 0) throw new ErroDeOrcamento("O arquivo de compartilhamento não tem nenhuma despesa.");
        var pessoas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var nome in arquivo.Pessoas) { RegrasDoOrcamento.Texto(nome, "Nome", 80); if (nome != nome.Trim() || !pessoas.Add(nome)) throw new ErroDeOrcamento("O arquivo traz um nome de pessoa repetido ou inválido."); }
        var categorias = new HashSet<string>(RegrasDoOrcamento.Categorias, StringComparer.OrdinalIgnoreCase);
        foreach (var nome in arquivo.CategoriasProprias) { RegrasDoOrcamento.Texto(nome, "Categoria", MotorDeCategorias.TamanhoMaximo); if (nome != nome.Trim() || !categorias.Add(nome)) throw new ErroDeOrcamento("O arquivo traz uma categoria repetida ou inválida."); }
        var origens = new HashSet<Guid>();
        foreach (var e in arquivo.Despesas)
        {
            if (e is null || e.IdDeOrigem == Guid.Empty || !origens.Add(e.IdDeOrigem)) throw new ErroDeOrcamento("O arquivo traz uma despesa sem identificador de origem ou com identificador repetido.");
            if (e.Participantes is null || e.Participantes.Count > 200 || e.Participantes.Any(n => n is null || !pessoas.Contains(n)) || (e.PagaPor is not null && !pessoas.Contains(e.PagaPor)))
                throw new ErroDeOrcamento("O arquivo cita uma pessoa que não está na sua lista de pessoas.");
            if (e.Itens is null || e.Itens.Count > 500 || e.Itens.Any(i => i is null)) throw new ErroDeOrcamento("O arquivo traz uma lista de itens inválida.");
            if (e.Categoria is null || !categorias.Contains(e.Categoria)) throw new ErroDeOrcamento("O arquivo usa uma categoria que não está na sua lista de categorias.");
        }
    }
    /// <summary>
    /// Valida e grava o arquivo de compartilhamento de forma atômica (temporário + troca), sem tocar na base do aplicativo.
    /// </summary>
    public static void Salvar(string caminho, ArquivoDeCompartilhamento arquivo)
    {
        Validar(arquivo); var json = JsonSerializer.Serialize(arquivo, ArmazenamentoLocal.OpcoesJson);
        if (Encoding.UTF8.GetByteCount(json) > MaximoDeBytes) throw new ErroDeOrcamento("O arquivo de compartilhamento ficaria grande demais. Selecione menos despesas.");
        ArmazenamentoLocal.GravarDeFormaAtomica(caminho, json);
    }
    /// <summary>
    /// Lê e valida um arquivo de compartilhamento com as mesmas proteções do backup: limite de tamanho, profundidade limitada e
    /// recusa de campos desconhecidos. Um backup escolhido por engano é recusado com uma mensagem própria.
    /// </summary>
    public static ArquivoDeCompartilhamento Ler(string caminho)
    {
        ArquivoDeCompartilhamento arquivo;
        using (var fluxo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (fluxo.Length > MaximoDeBytes) throw new ErroDeOrcamento("O arquivo de compartilhamento deve ter no máximo 5 MB.");
            try { arquivo = JsonSerializer.Deserialize<ArquivoDeCompartilhamento>(fluxo, ArmazenamentoLocal.OpcoesJson) ?? throw new ErroDeOrcamento("Arquivo vazio."); }
            catch (JsonException)
            {
                fluxo.Dispose();
                if (PareceBackup(caminho)) throw new ErroDeOrcamento("Este arquivo é um backup completo, não um compartilhamento de despesas. Para restaurar um backup, use “Importar backup” em Ajustes. Nada foi alterado.");
                throw new ErroDeOrcamento("O arquivo não é um compartilhamento de despesas válido do BankManager. Nada foi alterado.");
            }
        }
        Validar(arquivo); return arquivo;
    }
    /// <summary>Diz se o arquivo tem cara de compartilhamento (campo Kind igual a "share"), para uma mensagem de erro mais clara.</summary>
    internal static bool PareceCompartilhamento(string caminho) => Espiar(caminho, raiz => raiz.TryGetProperty("Kind", out var tipo) && tipo.ValueKind == JsonValueKind.String && tipo.GetString() == ArquivoDeCompartilhamento.TipoCompartilhamento);
    /// <summary>Diz se o arquivo tem cara de backup (tem Version e não tem Kind).</summary>
    internal static bool PareceBackup(string caminho) => Espiar(caminho, raiz => raiz.TryGetProperty("Version", out _) && !raiz.TryGetProperty("Kind", out _));
    // Abre o JSON só para olhar os campos do topo; qualquer falha significa "não reconhecido".
    private static bool Espiar(string caminho, Func<JsonElement, bool> teste)
    {
        try
        {
            if (new FileInfo(caminho).Length > 20 * 1024 * 1024) return false;
            using var documento = JsonDocument.Parse(File.ReadAllBytes(caminho), new JsonDocumentOptions { MaxDepth = 32 });
            return documento.RootElement.ValueKind == JsonValueKind.Object && teste(documento.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return false; }
    }
    /// <summary>
    /// Categorias próprias do arquivo que ainda não existem na base (comparando sem diferenciar maiúsculas).
    /// </summary>
    public static List<string> CategoriasQueFaltam(DadosDoOrcamento dados, ArquivoDeCompartilhamento arquivo) =>
        arquivo.CategoriasProprias.Where(c => !RegrasDoOrcamento.TodasAsCategorias(dados).Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
    /// <summary>
    /// Sugere um mapeamento: cada nome vira a pessoa cadastrada com o mesmo nome (ignorando maiúsculas e acentos) ou um cadastro
    /// novo; categorias que faltam são criadas enquanto couberem no limite. É só uma sugestão: a tela sempre a mostra para confirmação.
    /// </summary>
    public static MapeamentoDoCompartilhamento Sugerir(DadosDoOrcamento dados, ArquivoDeCompartilhamento arquivo)
    {
        var pessoas = new Dictionary<string, EscolhaDaPessoa>();
        foreach (var nome in arquivo.Pessoas)
        {
            var correspondente = dados.Pessoas.FirstOrDefault(p => BuscaDeDespesas.Simplificar(p.Nome) == BuscaDeDespesas.Simplificar(nome));
            pessoas[nome] = correspondente is null ? new EscolhaDaPessoa(AcaoDaPessoa.Criar) : new EscolhaDaPessoa(AcaoDaPessoa.Existente, correspondente.Id);
        }
        var vagas = MotorDeCategorias.MaximoDeProprias - dados.CategoriasProprias.Count; var categorias = new Dictionary<string, bool>();
        foreach (var nome in CategoriasQueFaltam(dados, arquivo)) categorias[nome] = vagas-- > 0;
        return new MapeamentoDoCompartilhamento { Pessoas = pessoas, CriarCategorias = categorias };
    }
    /// <summary>
    /// Calcula a importação sem alterar <paramref name="dados"/>: trabalha em uma cópia profunda, adiciona as despesas e valida o
    /// orçamento inteiro. Serve de prévia e também de base para o salvamento (tudo ou nada).
    /// </summary>
    /// <returns>A nova base (com tudo o que já existia, intacto, mais as despesas adicionadas) e o resumo do que foi feito.</returns>
    public static (DadosDoOrcamento Next, ResultadoDaImportacao Result) Importar(DadosDoOrcamento dados, ArquivoDeCompartilhamento arquivo, MapeamentoDoCompartilhamento mapeamento)
    {
        var proximo = ArmazenamentoLocal.Clonar(dados); var resultado = Aplicar(proximo, arquivo, mapeamento); RegrasDoOrcamento.Validar(proximo); return (proximo, resultado);
    }
    /// <summary>
    /// Adiciona as despesas do arquivo a <paramref name="dados"/> (que deve ser uma cópia de trabalho). Não remove nem altera rendas,
    /// metas, acertos ou despesas existentes. Uma exceção significa que nada deve ser aproveitado.
    /// </summary>
    public static ResultadoDaImportacao Aplicar(DadosDoOrcamento dados, ArquivoDeCompartilhamento arquivo, MapeamentoDoCompartilhamento mapeamento)
    {
        Validar(arquivo);
        var existente = dados.Pessoas.ToDictionary(p => p.Id);
        var pessoasCriadas = new Dictionary<string, Pessoa>(StringComparer.OrdinalIgnoreCase); var categoriasCriadas = new List<string>();
        // Pessoa do arquivo → Id local (nulo quando ignorada). Cadastros novos nascem só quando a primeira despesa precisa deles.
        Guid? Resolver(string nome)
        {
            if (!mapeamento.Pessoas.TryGetValue(nome, out var escolha)) throw new ErroDeOrcamento($"Escolha o que fazer com “{nome}”: usar uma pessoa cadastrada, criar um cadastro ou ignorar.");
            switch (escolha.Acao)
            {
                case AcaoDaPessoa.Ignorar: return null;
                case AcaoDaPessoa.Existente:
                    if (escolha.IdDaPessoa is not { } id || !existente.ContainsKey(id)) throw new ErroDeOrcamento($"A pessoa escolhida para “{nome}” não existe mais.");
                    return id;
                default:
                    // Se já há alguém com esse nome exato, usa o cadastro existente em vez de tentar criar um nome repetido.
                    var mesmo = dados.Pessoas.FirstOrDefault(p => string.Equals(p.Nome.Trim(), nome, StringComparison.OrdinalIgnoreCase));
                    if (mesmo is not null) return mesmo.Id;
                    var pessoa = new Pessoa { Nome = nome }; dados.Pessoas.Add(pessoa); pessoasCriadas[nome] = pessoa; return pessoa.Id;
            }
        }
        // Categoria do arquivo → categoria local: padrão, própria já existente, criada agora ou "Outros".
        string Categoria(string nome)
        {
            var conhecidas = RegrasDoOrcamento.TodasAsCategorias(dados).FirstOrDefault(c => string.Equals(c, nome, StringComparison.OrdinalIgnoreCase));
            if (conhecidas is not null) return conhecidas;
            if (!mapeamento.CriarCategorias.GetValueOrDefault(nome)) return "Outros";
            if (dados.CategoriasProprias.Count >= MotorDeCategorias.MaximoDeProprias) throw new ErroDeOrcamento($"O limite de {MotorDeCategorias.MaximoDeProprias} categorias próprias seria ultrapassado. Escolha “Outros” para algumas categorias do arquivo.");
            dados.CategoriasProprias.Add(nome); categoriasCriadas.Add(nome); return nome;
        }
        // Já existe aqui: mesmo Id (a despesa nasceu nesta base) ou mesma origem (já foi importada antes).
        var conhecidas = dados.Despesas.Select(e => e.Id).Concat(dados.Despesas.Where(e => e.IdDeOrigem is not null).Select(e => e.IdDeOrigem!.Value)).ToHashSet();
        var grupos = new Dictionary<Guid, Guid>(); var adicionadas = new List<Despesa>(); var erros = new List<string>(); var linhas = new List<LinhaDaImportacao>(); var duplicadas = 0; var proporcional = 0;
        for (var indice = 0; indice < arquivo.Despesas.Count; indice++)
        {
            var compartilhada = arquivo.Despesas[indice];
            var jaExiste = conhecidas.Contains(compartilhada.IdDeOrigem);
            if (jaExiste && !mapeamento.ImportarDuplicadas && !mapeamento.ImportarMesmoAssim.Contains(compartilhada.IdDeOrigem)) { duplicadas++; linhas.Add(new LinhaDaImportacao(compartilhada, SituacaoDaLinha.Pulada, true)); continue; }
            var resolvidos = compartilhada.Participantes.Select(nome => (Nome: nome, Id: Resolver(nome))).Where(p => p.Id is not null).ToList();
            // Duas pessoas do arquivo ligadas à mesma pessoa daqui mudariam a divisão sem avisar: quem importa precisa escolher de novo.
            var unidas = resolvidos.GroupBy(p => p.Id).FirstOrDefault(g => g.Count() > 1);
            if (unidas is not null) throw new ErroDeOrcamento($"“{unidas.First().Nome}” e “{unidas.Last().Nome}” foram ligados à mesma pessoa, mas dividem a despesa “{compartilhada.Titulo}”. Escolha pessoas diferentes ou ignore uma delas.");
            var participantes = resolvidos.Select(p => p.Id!.Value).ToList();
            if (participantes.Count == 0)
            {
                // Todos os responsáveis foram ignorados: a despesa precisa de alguém, e essa escolha é de quem importa.
                if (mapeamento.ResponsavelPadrao is not { } responsavelPadrao || !existente.ContainsKey(responsavelPadrao)) throw new ErroDeOrcamento("Algumas despesas ficariam sem responsável. Escolha quem assume essas despesas.");
                participantes = [responsavelPadrao];
            }
            var divisao = compartilhada.Divisao == ModoDeDivisao.RendaDisponivel && mapeamento.IgualEmVezDeProporcional ? ModoDeDivisao.Igual : compartilhada.Divisao;
            // Uma despesa individual cujo responsável mudou continua individual, agora com a pessoa que restou.
            if (divisao == ModoDeDivisao.Individual && participantes.Count > 1) participantes = [participantes[0]];
            Guid? grupo = compartilhada.GrupoDeParcelas is { } antigo ? grupos.TryGetValue(antigo, out var mapeado) ? mapeado : grupos[antigo] = Guid.NewGuid() : null;
            var despesa = new Despesa
            {
                Titulo = compartilhada.Titulo?.Trim() ?? "", Categoria = Categoria(compartilhada.Categoria), Vencimento = compartilhada.Vencimento, Valor = compartilhada.Valor, Itens = compartilhada.Itens.Select(i => i with { }).ToList(),
                GrupoDeParcelas = grupo, NumeroDaParcela = compartilhada.NumeroDaParcela, TotalDeParcelas = compartilhada.TotalDeParcelas, Divisao = divisao, Participantes = participantes,
                Paga = compartilhada.Paga, DataDoPagamento = compartilhada.Paga ? compartilhada.DataDoPagamento : null, PagaPor = compartilhada.Paga && compartilhada.PagaPor is not null ? Resolver(compartilhada.PagaPor) : null, IdDeOrigem = compartilhada.IdDeOrigem
            };
            // Confere a despesa sozinha, com as pessoas e categorias locais: um erro vira uma linha na prévia e as outras seguem.
            try { RegrasDoOrcamento.Validar(new DadosDoOrcamento { Pessoas = dados.Pessoas, CategoriasProprias = dados.CategoriasProprias, Despesas = [despesa] }); }
            catch (ErroDeOrcamento ex) { erros.Add($"Despesa {indice + 1} ({(string.IsNullOrWhiteSpace(compartilhada.Titulo) ? "sem descrição" : compartilhada.Titulo.Trim())}): {ex.Message}"); linhas.Add(new LinhaDaImportacao(compartilhada, SituacaoDaLinha.Erro, jaExiste, Erro: ex.Message)); continue; }
            dados.Despesas.Add(despesa); adicionadas.Add(despesa); linhas.Add(new LinhaDaImportacao(compartilhada, SituacaoDaLinha.Adicionada, jaExiste, despesa)); if (divisao == ModoDeDivisao.RendaDisponivel) proporcional++;
        }
        // Cadastros e categorias criados para despesas que acabaram não entrando são desfeitos.
        var pessoasUsadas = adicionadas.SelectMany(e => e.PagaPor is { } p ? e.Participantes.Append(p) : e.Participantes).ToHashSet();
        foreach (var (nome, pessoa) in pessoasCriadas.Where(c => !pessoasUsadas.Contains(c.Value.Id)).ToList()) { dados.Pessoas.Remove(pessoa); pessoasCriadas.Remove(nome); }
        foreach (var nome in categoriasCriadas.Where(c => adicionadas.All(e => e.Categoria != c)).ToList()) { dados.CategoriasProprias.Remove(nome); categoriasCriadas.Remove(nome); }
        return new ResultadoDaImportacao(adicionadas, duplicadas, erros, pessoasCriadas.Values.Select(p => p.Nome).ToList(), categoriasCriadas, proporcional, linhas);
    }
}
