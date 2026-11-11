using System.Globalization;

namespace BankManager.Core;

/// <summary>
/// Define como uma despesa será distribuída entre seus participantes. A validação limita o modo individual a uma pessoa.
/// </summary>
public enum SplitMode
{
    /// <summary>Uma única pessoa assume todo o valor.</summary>
    Individual,
    /// <summary>Os participantes assumem partes iguais, com ajuste dos centavos restantes.</summary>
    Equal,
    /// <summary>Os pesos são a renda mensal menos despesas individuais, sem pesos negativos.</summary>
    AvailableIncome
}
/// <summary>
/// Pessoa que pode receber renda e assumir despesas. Seu identificador preserva as referências ao renomeá-la.
/// </summary>
public sealed record Person
{
    /// <summary>Identificador estável gerado no cadastro; as rendas e os participantes referenciam este valor.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>Nome exibido nas telas; deve ser preenchido e ser único, ignorando maiúsculas e espaços nas extremidades.</summary>
    public string Name { get; set; } = "";
}
/// <summary>
/// Renda líquida de uma pessoa em um mês específico; não é repetida automaticamente nos meses seguintes.
/// </summary>
public sealed record MonthlyIncome
{
    /// <summary>Pessoa à qual a renda pertence; deve existir em BudgetData.People.</summary>
    public Guid PersonId { get; set; }
    /// <summary>Competência da renda, representada obrigatoriamente pelo primeiro dia do mês.</summary>
    public DateOnly Month { get; set; }
    /// <summary>Renda líquida mensal em reais; zero representa ausência de renda informada como tal.</summary>
    public decimal Amount { get; set; }
}
/// <summary>
/// Item de uma compra detalhada. O valor da despesa corresponde à soma dos totais arredondados dos itens.
/// </summary>
public sealed record ExpenseItem
{
    /// <summary>Descrição do produto ou serviço, limitada a 100 caracteres.</summary>
    public string Name { get; set; } = "";
    /// <summary>Quantidade positiva, com até três casas decimais, inclusive para compras por peso.</summary>
    public decimal Quantity { get; set; } = 1;
    /// <summary>Preço de uma unidade em reais, positivo e com até duas casas decimais.</summary>
    public decimal UnitPrice { get; set; }
    /// <summary>Quantidade multiplicada pelo preço, arredondada para centavos; empates são afastados de zero.</summary>
    public decimal Total => decimal.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);
}
/// <summary>
/// Lançamento com vencimento, responsáveis e situação de pagamento. Cada parcela é um lançamento independente.
/// </summary>
public sealed record Expense
{
    /// <summary>Identificador único deste lançamento, não compartilhado entre parcelas.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>Descrição curta da conta ou compra, usada na listagem e na busca.</summary>
    public string Title { get; set; } = "";
    /// <summary>Uma das categorias admitidas por BudgetRules.Categories.</summary>
    public string Category { get; set; } = "Outros";
    /// <summary>Data de vencimento, que determina o mês do lançamento e a renda usada no rateio.</summary>
    public DateOnly DueDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    /// <summary>Valor deste lançamento; em uma dívida já parcelada, corresponde somente a esta parcela.</summary>
    public decimal Amount { get; set; }
    /// <summary>Indica quitação integral do lançamento; não registra pagamentos parciais nem quitação por participante.</summary>
    public bool Paid { get; set; }
    /// <summary>Regra de distribuição aplicada aos participantes selecionados.</summary>
    public SplitMode Split { get; set; }
    /// <summary>Identificadores dos responsáveis, sem repetições; a lista precisa conter pelo menos uma pessoa.</summary>
    public List<Guid> Participants { get; set; } = [];
    /// <summary>Itens detalhados de uma compra à vista; vazia quando o valor é informado diretamente.</summary>
    public List<ExpenseItem> Items { get; set; } = [];
    /// <summary>Identificador comum às parcelas geradas juntas; nulo para uma compra sem parcelamento.</summary>
    public Guid? InstallmentGroup { get; set; }
    /// <summary>Posição desta parcela, iniciada em 1, dentro do parcelamento original.</summary>
    public int InstallmentNumber { get; set; } = 1;
    /// <summary>Quantidade de parcelas da compra original; vale 1 para lançamentos à vista.</summary>
    public int InstallmentCount { get; set; } = 1;
    /// <summary>
    /// Pessoa que efetivamente pagou a despesa. Só faz sentido com <see cref="Paid"/> verdadeiro; nulo significa "não informado"
    /// (inclusive nos lançamentos antigos) e deixa a despesa fora do acerto de contas.
    /// </summary>
    public Guid? PaidBy { get; set; }
    /// <summary>Recorrência que gerou este lançamento (aluguel, assinatura...); nulo para lançamentos comuns. Veja <see cref="Recurrence"/>.</summary>
    public Guid? RecurrenceId { get; set; }
    /// <summary>
    /// Mês (primeiro dia) para o qual a recorrência gerou este lançamento. Fica fixo mesmo que o vencimento seja editado
    /// para outro mês, para que a geração automática não recrie a ocorrência. Nulo se, e somente se, <see cref="RecurrenceId"/> for nulo.
    /// </summary>
    public DateOnly? RecurrenceMonth { get; set; }
}
/// <summary>
/// Pagamento feito entre duas pessoas para quitar (total ou parcialmente) o que uma deve à outra.
/// Abate o saldo calculado por <see cref="SettlementEngine"/>; não é uma despesa.
/// </summary>
public sealed record Settlement
{
    /// <summary>Identificador estável, usado para excluir um acerto registrado por engano.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>Pessoa que pagou (quem devia).</summary>
    public Guid PayerId { get; set; }
    /// <summary>Pessoa que recebeu (quem estava sendo paga). Deve ser diferente de <see cref="PayerId"/>.</summary>
    public Guid ReceiverId { get; set; }
    /// <summary>Valor em reais, positivo e com até duas casas decimais.</summary>
    public decimal Amount { get; set; }
    /// <summary>Dia em que o acerto aconteceu.</summary>
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
}
/// <summary>
/// Modelo de uma despesa que se repete todo mês (aluguel, internet, assinaturas). O aplicativo cria lançamentos comuns,
/// um por mês, a partir deste modelo; cada um pode ser editado, pago ou excluído sem afetar os outros.
/// </summary>
public sealed record Recurrence
{
    /// <summary>Identificador estável; os lançamentos gerados o referenciam em <see cref="Expense.RecurrenceId"/>.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>Descrição copiada para cada lançamento gerado.</summary>
    public string Title { get; set; } = "";
    /// <summary>Categoria copiada para cada lançamento gerado.</summary>
    public string Category { get; set; } = "Outros";
    /// <summary>Valor mensal em reais, positivo e com até duas casas decimais.</summary>
    public decimal Amount { get; set; }
    /// <summary>Dia do vencimento, de 1 a 31. Em meses mais curtos vale o último dia (31 vira 28 ou 29 em fevereiro).</summary>
    public int DayOfMonth { get; set; } = 1;
    /// <summary>Regra de divisão aplicada a cada lançamento gerado.</summary>
    public SplitMode Split { get; set; }
    /// <summary>Responsáveis de cada lançamento gerado; as mesmas regras de <see cref="Expense.Participants"/> valem aqui.</summary>
    public List<Guid> Participants { get; set; } = [];
    /// <summary>Primeiro mês da recorrência, representado pelo primeiro dia do mês.</summary>
    public DateOnly StartMonth { get; set; }
    /// <summary>Último mês em que a recorrência gera lançamentos (primeiro dia do mês); nulo enquanto estiver ativa.</summary>
    public DateOnly? EndMonth { get; set; }
    /// <summary>Meses cujo lançamento o usuário excluiu de propósito; a geração automática não os recria.</summary>
    public List<DateOnly> SkippedMonths { get; set; } = [];
}
/// <summary>
/// Raiz do arquivo JSON local. Reúne os registros de todos os meses e a versão do formato de persistência.
/// </summary>
public sealed record BudgetData
{
    /// <summary>Versão do esquema atual. A 2 acrescentou as despesas recorrentes; arquivos da versão 1 são migrados ao ler.</summary>
    public const int CurrentVersion = 2;
    /// <summary>Versão do esquema JSON; formatos não suportados são rejeitados antes de importar ou salvar.</summary>
    public int Version { get; set; } = CurrentVersion;
    /// <summary>Cadastro de pessoas compartilhado entre todos os meses.</summary>
    public List<Person> People { get; set; } = [];
    /// <summary>Histórico de renda, com no máximo um registro por pessoa e competência.</summary>
    public List<MonthlyIncome> Incomes { get; set; } = [];
    /// <summary>Histórico e compromissos futuros, incluindo parcelas criadas antecipadamente.</summary>
    public List<Expense> Expenses { get; set; } = [];
    /// <summary>Modelos de despesas mensais. Ausente nos arquivos da versão 1, onde equivale a uma lista vazia.</summary>
    public List<Recurrence> Recurrences { get; set; } = [];
    /// <summary>Acertos de contas já feitos entre pessoas. Ausente em arquivos antigos, onde equivale a uma lista vazia.</summary>
    public List<Settlement> Settlements { get; set; } = [];
}
/// <summary>
/// Centraliza apresentação, leitura e limites de valores monetários, usando decimal para evitar imprecisão binária.
/// </summary>
public static class Money
{
    /// <summary>
    /// Cultura brasileira compartilhada: vírgula decimal e apresentação em reais.
    /// </summary>
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");
    /// <summary>
    /// Formata um valor para exibição, com símbolo de real e duas casas decimais; não serve como entrada de Parse.
    /// </summary>
    public static string Format(decimal value) => value.ToString("C2", Culture);
    /// <summary>
    /// Converte uma entrada como 1500,50 e valida seus limites, sem aceitar pontos de milhar ou arredondar casas extras.
    /// </summary>
    /// <param name="input">Texto digitado no campo, sem símbolo de moeda.</param>
    /// <param name="label">Nome do campo usado na mensagem de validação.</param>
    /// <param name="positive">Quando verdadeiro, rejeita zero; rendas permitem zero, despesas não.</param>
    /// <exception cref="BudgetException">A sintaxe ou o valor informado não é aceito.</exception>
    public static decimal Parse(string? input, string label, bool positive = false)
    {
        var s = input?.Trim() ?? "";
        // A expressão limita a sintaxe antes da conversão; casas extras são rejeitadas, não arredondadas.
        if (!System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d{1,9}(,\d{1,2})?$") ||
            !decimal.TryParse(s, NumberStyles.AllowDecimalPoint, Culture, out var value))
            throw new BudgetException($"{label}: use um número como 1500,50, sem pontos ou símbolo R$.");
        Validate(value, label, positive);
        return value;
    }
    /// <summary>
    /// Exige um valor não negativo de até 999.999.999,99 e no máximo duas casas decimais; pode exigir valor positivo.
    /// </summary>
    public static void Validate(decimal value, string label, bool positive = false)
    {
        if (value < 0 || value > 999_999_999.99m || (positive && value == 0) || decimal.Round(value, 2) != value)
            throw new BudgetException($"{label}: informe {(positive ? "um valor maior que zero" : "um valor a partir de zero")}, com até 2 casas decimais e no máximo R$ 999.999.999,99.");
    }
}
/// <summary>
/// Erro esperado de regra de negócio. Sua mensagem é escrita para ser exibida diretamente ao usuário.
/// </summary>
public sealed class BudgetException(string message) : Exception(message);
/// <summary>
/// Convenções de datas utilizadas para agrupar rendas, despesas e previsões por mês.
/// </summary>
public static class Dates
{
    /// <summary>
    /// Normaliza qualquer dia para o primeiro dia do mesmo mês, permitindo comparar competências mensais.
    /// </summary>
    public static DateOnly Month(DateOnly date) => new(date.Year, date.Month, 1);
}
