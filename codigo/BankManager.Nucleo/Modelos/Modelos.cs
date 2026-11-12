using System.Globalization;
using System.Text.Json.Serialization;

namespace BankManager.Nucleo;

/// <summary>
/// Define como uma despesa será distribuída entre seus participantes. A validação limita o modo individual a uma pessoa.
/// </summary>
public enum ModoDeDivisao
{
    /// <summary>Uma única pessoa assume todo o valor.</summary>
    Individual,
    /// <summary>Os participantes assumem partes iguais, com ajuste dos centavos restantes.</summary>
    Igual,
    /// <summary>Os pesos são a renda mensal menos despesas individuais, sem pesos negativos.</summary>
    RendaDisponivel
}
/// <summary>
/// Pessoa que pode receber renda e assumir despesas. Seu identificador preserva as referências ao renomeá-la.
/// </summary>
public sealed record Pessoa
{
    /// <summary>Identificador estável gerado no cadastro; as rendas e os participantes referenciam este valor.</summary>
    [JsonPropertyName("Id")] public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>Nome exibido nas telas; deve ser preenchido e ser único, ignorando maiúsculas e espaços nas extremidades.</summary>
    [JsonPropertyName("Name")] public string Nome { get; set; } = "";
}
/// <summary>
/// Uma linha de renda líquida de uma pessoa em um mês específico (salário, extra, 13º); a renda do mês é a soma das linhas.
/// Não é repetida automaticamente nos meses seguintes.
/// </summary>
public sealed record RendaMensal
{
    /// <summary>Identificador estável desta linha de renda. Arquivos antigos não o tinham: ele é gerado ao ler e gravado no próximo salvamento.</summary>
    [JsonPropertyName("Id")] public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>Nome da linha ("Salário", "Extra", "13º"). Arquivos antigos, com uma renda por mês, aparecem como "Renda".</summary>
    [JsonPropertyName("Label")] public string Rotulo { get; set; } = "Renda";
    /// <summary>Pessoa à qual a renda pertence; deve existir em DadosDoOrcamento.Pessoas.</summary>
    [JsonPropertyName("PersonId")] public Guid IdDaPessoa { get; set; }
    /// <summary>Competência da renda, representada obrigatoriamente pelo primeiro dia do mês.</summary>
    [JsonPropertyName("Month")] public DateOnly Mes { get; set; }
    /// <summary>Renda líquida mensal em reais; zero representa ausência de renda informada como tal.</summary>
    [JsonPropertyName("Amount")] public decimal Valor { get; set; }
}
/// <summary>
/// Item de uma compra detalhada. O valor da despesa corresponde à soma dos totais arredondados dos itens.
/// </summary>
public sealed record ItemDaDespesa
{
    /// <summary>Descrição do produto ou serviço, limitada a 100 caracteres.</summary>
    [JsonPropertyName("Name")] public string Nome { get; set; } = "";
    /// <summary>Quantidade positiva, com até três casas decimais, inclusive para compras por peso.</summary>
    [JsonPropertyName("Quantity")] public decimal Quantidade { get; set; } = 1;
    /// <summary>Preço de uma unidade em reais, positivo e com até duas casas decimais.</summary>
    [JsonPropertyName("UnitPrice")] public decimal PrecoUnitario { get; set; }
    /// <summary>Quantidade multiplicada pelo preço, arredondada para centavos; empates são afastados de zero.</summary>
    [JsonPropertyName("Total")] public decimal Total => decimal.Round(Quantidade * PrecoUnitario, 2, MidpointRounding.AwayFromZero);
}
/// <summary>
/// Lançamento com vencimento, responsáveis e situação de pagamento. Cada parcela é um lançamento independente.
/// </summary>
public sealed record Despesa
{
    /// <summary>Identificador único deste lançamento, não compartilhado entre parcelas.</summary>
    [JsonPropertyName("Id")] public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>Descrição curta da conta ou compra, usada na listagem e na busca.</summary>
    [JsonPropertyName("Title")] public string Titulo { get; set; } = "";
    /// <summary>Uma categoria padrão (<see cref="RegrasDoOrcamento.Categorias"/>) ou uma criada pelo usuário (<see cref="DadosDoOrcamento.CategoriasProprias"/>).</summary>
    [JsonPropertyName("Category")] public string Categoria { get; set; } = "Outros";
    /// <summary>Data de vencimento, que determina o mês do lançamento e a renda usada no rateio.</summary>
    [JsonPropertyName("DueDate")] public DateOnly Vencimento { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    /// <summary>Valor deste lançamento; em uma dívida já parcelada, corresponde somente a esta parcela.</summary>
    [JsonPropertyName("Amount")] public decimal Valor { get; set; }
    /// <summary>Indica quitação integral do lançamento; não registra pagamentos parciais nem quitação por participante.</summary>
    [JsonPropertyName("Paid")] public bool Paga { get; set; }
    /// <summary>Regra de distribuição aplicada aos participantes selecionados.</summary>
    [JsonPropertyName("Split")] public ModoDeDivisao Divisao { get; set; }
    /// <summary>Identificadores dos responsáveis, sem repetições; a lista precisa conter pelo menos uma pessoa.</summary>
    [JsonPropertyName("Participants")] public List<Guid> Participantes { get; set; } = [];
    /// <summary>Itens detalhados de uma compra à vista; vazia quando o valor é informado diretamente.</summary>
    [JsonPropertyName("Items")] public List<ItemDaDespesa> Itens { get; set; } = [];
    /// <summary>Identificador comum às parcelas geradas juntas; nulo para uma compra sem parcelamento.</summary>
    [JsonPropertyName("InstallmentGroup")] public Guid? GrupoDeParcelas { get; set; }
    /// <summary>Posição desta parcela, iniciada em 1, dentro do parcelamento original.</summary>
    [JsonPropertyName("InstallmentNumber")] public int NumeroDaParcela { get; set; } = 1;
    /// <summary>Quantidade de parcelas da compra original; vale 1 para lançamentos à vista.</summary>
    [JsonPropertyName("InstallmentCount")] public int TotalDeParcelas { get; set; } = 1;
    /// <summary>
    /// Pessoa que efetivamente pagou a despesa. Só faz sentido com <see cref="Paga"/> verdadeiro; nulo significa "não informado"
    /// (inclusive nos lançamentos antigos) e deixa a despesa fora do acerto de contas.
    /// </summary>
    [JsonPropertyName("PaidBy")] public Guid? PagaPor { get; set; }
    /// <summary>
    /// Dia em que a despesa foi paga (esquema versão 3). Só existe em despesas pagas e é opcional: despesas pagas em versões
    /// anteriores não têm essa informação, e as telas usam o vencimento como referência, sem inventar uma data.
    /// </summary>
    [JsonPropertyName("PaidDate")] public DateOnly? DataDoPagamento { get; set; }
    /// <summary>
    /// Identificador de origem de uma despesa recebida por um arquivo de compartilhamento (esquema versão 3): o Id que ela
    /// tinha na base de quem a enviou. Serve para reconhecer a mesma despesa em uma nova importação e não duplicá-la.
    /// </summary>
    [JsonPropertyName("OriginId")] public Guid? IdDeOrigem { get; set; }
    /// <summary>
    /// Data usada nas listas de movimentações: a do pagamento, quando informada; caso contrário, o vencimento.
    /// </summary>
    [JsonIgnore]
    public DateOnly DataDeReferencia => DataDoPagamento ?? Vencimento;
    /// <summary>Recorrência que gerou este lançamento (aluguel, assinatura...); nulo para lançamentos comuns. Veja <see cref="Recorrencia"/>.</summary>
    [JsonPropertyName("RecurrenceId")] public Guid? IdDaRecorrencia { get; set; }
    /// <summary>
    /// Mês (primeiro dia) para o qual a recorrência gerou este lançamento. Fica fixo mesmo que o vencimento seja editado
    /// para outro mês, para que a geração automática não recrie a ocorrência. Nulo se, e somente se, <see cref="IdDaRecorrencia"/> for nulo.
    /// </summary>
    [JsonPropertyName("RecurrenceMonth")] public DateOnly? MesDaRecorrencia { get; set; }
}
/// <summary>
/// Limite mensal de gasto em uma categoria (por exemplo, Mercado: R$ 800). Vale para todos os meses; há no máximo uma meta por categoria.
/// </summary>
public sealed record MetaDeCategoria
{
    /// <summary>Categoria (padrão ou própria) à qual a meta se aplica.</summary>
    [JsonPropertyName("Category")] public string Categoria { get; set; } = "";
    /// <summary>Limite mensal em reais, positivo e com até duas casas decimais.</summary>
    [JsonPropertyName("Amount")] public decimal Valor { get; set; }
}
/// <summary>
/// Pagamento feito entre duas pessoas para quitar (total ou parcialmente) o que uma deve à outra.
/// Abate o saldo calculado por <see cref="MotorDeAcertos"/>; não é uma despesa.
/// </summary>
public sealed record Acerto
{
    /// <summary>Identificador estável, usado para excluir um acerto registrado por engano.</summary>
    [JsonPropertyName("Id")] public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>Pessoa que pagou (quem devia).</summary>
    [JsonPropertyName("PayerId")] public Guid IdDoPagador { get; set; }
    /// <summary>Pessoa que recebeu (quem estava sendo paga). Deve ser diferente de <see cref="IdDoPagador"/>.</summary>
    [JsonPropertyName("ReceiverId")] public Guid IdDoRecebedor { get; set; }
    /// <summary>Valor em reais, positivo e com até duas casas decimais.</summary>
    [JsonPropertyName("Amount")] public decimal Valor { get; set; }
    /// <summary>Dia em que o acerto aconteceu.</summary>
    [JsonPropertyName("Date")] public DateOnly Data { get; set; } = DateOnly.FromDateTime(DateTime.Today);
}
/// <summary>
/// Modelo de uma despesa que se repete todo mês (aluguel, internet, assinaturas). O aplicativo cria lançamentos comuns,
/// um por mês, a partir deste modelo; cada um pode ser editado, pago ou excluído sem afetar os outros.
/// </summary>
public sealed record Recorrencia
{
    /// <summary>Identificador estável; os lançamentos gerados o referenciam em <see cref="Despesa.IdDaRecorrencia"/>.</summary>
    [JsonPropertyName("Id")] public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>Descrição copiada para cada lançamento gerado.</summary>
    [JsonPropertyName("Title")] public string Titulo { get; set; } = "";
    /// <summary>Categoria copiada para cada lançamento gerado.</summary>
    [JsonPropertyName("Category")] public string Categoria { get; set; } = "Outros";
    /// <summary>Valor mensal em reais, positivo e com até duas casas decimais.</summary>
    [JsonPropertyName("Amount")] public decimal Valor { get; set; }
    /// <summary>Dia do vencimento, de 1 a 31. Em meses mais curtos vale o último dia (31 vira 28 ou 29 em fevereiro).</summary>
    [JsonPropertyName("DayOfMonth")] public int DiaDoMes { get; set; } = 1;
    /// <summary>Regra de divisão aplicada a cada lançamento gerado.</summary>
    [JsonPropertyName("Split")] public ModoDeDivisao Divisao { get; set; }
    /// <summary>Responsáveis de cada lançamento gerado; as mesmas regras de <see cref="Despesa.Participantes"/> valem aqui.</summary>
    [JsonPropertyName("Participants")] public List<Guid> Participantes { get; set; } = [];
    /// <summary>Primeiro mês da recorrência, representado pelo primeiro dia do mês.</summary>
    [JsonPropertyName("StartMonth")] public DateOnly MesInicial { get; set; }
    /// <summary>Último mês em que a recorrência gera lançamentos (primeiro dia do mês); nulo enquanto estiver ativa.</summary>
    [JsonPropertyName("EndMonth")] public DateOnly? MesFinal { get; set; }
    /// <summary>Meses cujo lançamento o usuário excluiu de propósito; a geração automática não os recria.</summary>
    [JsonPropertyName("SkippedMonths")] public List<DateOnly> MesesPulados { get; set; } = [];
}
/// <summary>
/// Raiz do arquivo JSON local. Reúne os registros de todos os meses e a versão do formato de persistência.
/// </summary>
public sealed record DadosDoOrcamento
{
    /// <summary>Versão do esquema atual. A 2 acrescentou as despesas recorrentes; arquivos da versão 1 são migrados ao ler.</summary>
    public const int VersaoAtual = 3;
    /// <summary>Versão do esquema JSON; formatos não suportados são rejeitados antes de importar ou salvar.</summary>
    [JsonPropertyName("Version")] public int Versao { get; set; } = VersaoAtual;
    /// <summary>Cadastro de pessoas compartilhado entre todos os meses.</summary>
    [JsonPropertyName("People")] public List<Pessoa> Pessoas { get; set; } = [];
    /// <summary>Histórico de renda: até 20 linhas, com nomes diferentes, por pessoa e competência.</summary>
    [JsonPropertyName("Incomes")] public List<RendaMensal> Rendas { get; set; } = [];
    /// <summary>Histórico e compromissos futuros, incluindo parcelas criadas antecipadamente.</summary>
    [JsonPropertyName("Expenses")] public List<Despesa> Despesas { get; set; } = [];
    /// <summary>Modelos de despesas mensais. Ausente nos arquivos da versão 1, onde equivale a uma lista vazia.</summary>
    [JsonPropertyName("Recurrences")] public List<Recorrencia> Recorrencias { get; set; } = [];
    /// <summary>Categorias criadas pelo usuário, além das padrão. Ausente em arquivos antigos, onde equivale a uma lista vazia.</summary>
    [JsonPropertyName("CustomCategories")] public List<string> CategoriasProprias { get; set; } = [];
    /// <summary>Metas mensais por categoria. Ausente em arquivos antigos, onde equivale a uma lista vazia.</summary>
    [JsonPropertyName("Goals")] public List<MetaDeCategoria> Metas { get; set; } = [];
    /// <summary>Acertos de contas já feitos entre pessoas. Ausente em arquivos antigos, onde equivale a uma lista vazia.</summary>
    [JsonPropertyName("Settlements")] public List<Acerto> Acertos { get; set; } = [];
}
/// <summary>
/// Centraliza apresentação, leitura e limites de valores monetários, usando decimal para evitar imprecisão binária.
/// </summary>
public static class Dinheiro
{
    /// <summary>
    /// Cultura brasileira compartilhada: vírgula decimal e apresentação em reais.
    /// </summary>
    public static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-BR");
    /// <summary>
    /// Formata um valor para exibição, com símbolo de real e duas casas decimais; não serve como entrada de Parse.
    /// </summary>
    public static string Formatar(decimal valorLido) => valorLido.ToString("C2", Cultura);
    /// <summary>
    /// Converte uma entrada como 1500,50 e valida seus limites, sem aceitar pontos de milhar ou arredondar casas extras.
    /// </summary>
    /// <param name="entrada">Texto digitado no campo, sem símbolo de moeda.</param>
    /// <param name="rotulo">Nome do campo usado na mensagem de validação.</param>
    /// <param name="positivo">Quando verdadeiro, rejeita zero; rendas permitem zero, despesas não.</param>
    /// <exception cref="ErroDeOrcamento">A sintaxe ou o valor informado não é aceito.</exception>
    public static decimal Interpretar(string? entrada, string rotulo, bool positivo = false)
    {
        var s = entrada?.Trim() ?? "";
        // A expressão limita a sintaxe antes da conversão; casas extras são rejeitadas, não arredondadas.
        if (!System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d{1,9}(,\d{1,2})?$") ||
            !decimal.TryParse(s, NumberStyles.AllowDecimalPoint, Cultura, out var valorLido))
            throw new ErroDeOrcamento($"{rotulo}: use um número como 1500,50, sem pontos ou símbolo R$.");
        Validar(valorLido, rotulo, positivo);
        return valorLido;
    }
    /// <summary>
    /// Exige um valor não negativo de até 999.999.999,99 e no máximo duas casas decimais; pode exigir valor positivo.
    /// </summary>
    public static void Validar(decimal valorLido, string rotulo, bool positivo = false)
    {
        if (valorLido < 0 || valorLido > 999_999_999.99m || (positivo && valorLido == 0) || decimal.Round(valorLido, 2) != valorLido)
            throw new ErroDeOrcamento($"{rotulo}: informe {(positivo ? "um valor maior que zero" : "um valor a partir de zero")}, com até 2 casas decimais e no máximo R$ 999.999.999,99.");
    }
}
/// <summary>
/// Erro esperado de regra de negócio. Sua mensagem é escrita para ser exibida diretamente ao usuário.
/// </summary>
public sealed class ErroDeOrcamento(string mensagem) : Exception(mensagem);
/// <summary>
/// Convenções de datas utilizadas para agrupar rendas, despesas e previsões por mês.
/// </summary>
public static class Datas
{
    /// <summary>
    /// Normaliza qualquer dia para o primeiro dia do mesmo mês, permitindo comparar competências mensais.
    /// </summary>
    public static DateOnly Mes(DateOnly data) => new(data.Year, data.Month, 1);
}
