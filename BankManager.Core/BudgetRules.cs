namespace BankManager.Core;

/// <summary>
/// Valida a integridade do orçamento inteiro, tanto na entrada de backups quanto antes de qualquer gravação.
/// </summary>
public static class BudgetRules
{
    /// <summary>
    /// Vocabulário fechado usado no formulário, nos filtros e nas previsões.
    /// </summary>
    public static readonly string[] Categories = ["Moradia", "Mercado", "Transporte", "Saúde", "Educação", "Lazer", "Serviços", "Outros"];
    /// <summary>
    /// Categorias disponíveis: as padrão seguidas das criadas pelo usuário.
    /// </summary>
    public static List<string> AllCategories(BudgetData data) => [.. Categories, .. data.CustomCategories];
    /// <summary>
    /// Verifica versão, limites, unicidade, referências e coerência financeira, lançando BudgetException na primeira falha.
    /// </summary>
    /// <remarks>Não corrige registros nem altera o orçamento recebido. Campos são verificados mesmo que a interface já os tenha validado.</remarks>
    public static void Validate(BudgetData data)
    {
        if (data.Version != BudgetData.CurrentVersion) throw new BudgetException("Versão de arquivo não suportada.");
        if (data.People is null || data.Incomes is null || data.Expenses is null || data.Recurrences is null || data.Recurrences.Count > 1_000 || data.Settlements is null || data.Settlements.Count > 100_000 || data.CustomCategories is null || data.People.Count > 200 || data.Incomes.Count > 100_000 || data.Expenses.Count > 100_000)
            throw new BudgetException("Arquivo inválido ou com registros demais.");
        // Conjuntos detectam repetições e permitem conferir referências de rendas e despesas às pessoas.
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in data.People)
        {
            if (p is null || p.Id == Guid.Empty || !ids.Add(p.Id)) throw new BudgetException("Identificador de pessoa inválido ou repetido.");
            Text(p.Name, "Nome", 80);
            if (!names.Add(p.Name.Trim())) throw new BudgetException("Já existe uma pessoa com esse nome.");
        }
        // Uma pessoa pode ter várias rendas no mês (salário, extra, 13º), desde que com nomes diferentes e em número razoável.
        var incomeIds = new HashSet<Guid>(); var incomeNames = new HashSet<(Guid, DateOnly, string)>(); var perMonth = new Dictionary<(Guid, DateOnly), int>();
        foreach (var income in data.Incomes)
        {
            if (income is null || !ids.Contains(income.PersonId) || income.Month.Day != 1 || income.Id == Guid.Empty || !incomeIds.Add(income.Id))
                throw new BudgetException("Registro de renda inválido ou repetido.");
            Date(income.Month); Text(income.Label, "Nome da renda", 40);
            if (!incomeNames.Add((income.PersonId, income.Month, income.Label.Trim().ToUpperInvariant()))) throw new BudgetException("Há duas rendas com o mesmo nome no mesmo mês. Use nomes diferentes, como Salário e Extra.");
            var key = (income.PersonId, income.Month); perMonth[key] = perMonth.GetValueOrDefault(key) + 1;
            if (perMonth[key] > 20) throw new BudgetException("Cada pessoa pode ter até 20 rendas por mês.");
            Money.Validate(income.Amount, "Renda");
        }
        // Categorias próprias: até 40, nomes de 1 a 30 caracteres sem espaços nas pontas e sem repetir (ignorando maiúsculas) as padrão nem entre si.
        if (data.CustomCategories.Count > CategoryEngine.MaxCustom) throw new BudgetException($"Use no máximo {CategoryEngine.MaxCustom} categorias próprias.");
        var categoryNames = new HashSet<string>(Categories, StringComparer.OrdinalIgnoreCase);
        foreach (var name in data.CustomCategories)
        {
            Text(name, "Categoria", CategoryEngine.MaxLength);
            if (name != name.Trim() || !categoryNames.Add(name)) throw new BudgetException("Categoria própria inválida ou com nome repetido.");
        }
        // Um acerto liga duas pessoas diferentes que existem, com valor positivo.
        var settlementIds = new HashSet<Guid>();
        foreach (var s in data.Settlements)
        {
            if (s is null || s.Id == Guid.Empty || !settlementIds.Add(s.Id) || !ids.Contains(s.PayerId) || !ids.Contains(s.ReceiverId) || s.PayerId == s.ReceiverId)
                throw new BudgetException("Acerto de contas inválido: escolha duas pessoas diferentes.");
            Money.Validate(s.Amount, "Valor do acerto", true); Date(s.Date);
        }
        // Modelos de recorrência vêm antes das despesas, que podem referenciá-los.
        var recurrenceIds = new HashSet<Guid>();
        foreach (var r in data.Recurrences)
        {
            if (r is null || r.Id == Guid.Empty || !recurrenceIds.Add(r.Id)) throw new BudgetException("Identificador de recorrência inválido ou repetido.");
            Text(r.Title, "Descrição", 120);
            if (!categoryNames.Contains(r.Category)) throw new BudgetException("Categoria inválida.");
            Money.Validate(r.Amount, "Valor da recorrência", true);
            if (r.DayOfMonth is < 1 or > 31) throw new BudgetException("Dia do vencimento da recorrência inválido: use de 1 a 31.");
            Sharing(r.Split, r.Participants, ids);
            if (r.StartMonth.Day != 1 || r.EndMonth is { Day: not 1 } || r.EndMonth is { } end && end < r.StartMonth) throw new BudgetException("Período da recorrência inválido.");
            Date(r.StartMonth); if (r.EndMonth is { } last) Date(last);
            if (r.SkippedMonths is null || r.SkippedMonths.Count > 1_200 || r.SkippedMonths.Any(m => m.Day != 1) || r.SkippedMonths.Distinct().Count() != r.SkippedMonths.Count) throw new BudgetException("Meses ignorados da recorrência inválidos.");
        }
        // Cada lançamento gerado por uma recorrência ocupa um mês exclusivo dela.
        var occurrences = new HashSet<(Guid, DateOnly)>();
        // Cada lançamento é validado individualmente; parcelas podem ser excluídas ou editadas separadamente.
        var expenseIds = new HashSet<Guid>();
        foreach (var e in data.Expenses)
        {
            if (e is null || e.Id == Guid.Empty || !expenseIds.Add(e.Id)) throw new BudgetException("Identificador de despesa inválido ou repetido.");
            Text(e.Title, "Descrição", 120);
            if (!categoryNames.Contains(e.Category)) throw new BudgetException("Categoria inválida.");
            Date(e.DueDate);
            Money.Validate(e.Amount, "Valor da despesa", true);
            // Referências inexistentes ou duplicadas causariam rateios ambíguos; o modo também precisa ser conhecido.
            Sharing(e.Split, e.Participants, ids, "Selecione as pessoas responsáveis pela despesa.");
            if (e.InstallmentCount is < 1 or > 120 || e.InstallmentNumber < 1 || e.InstallmentNumber > e.InstallmentCount || (e.InstallmentCount > 1 && (e.InstallmentGroup is null || e.InstallmentGroup == Guid.Empty)))
                throw new BudgetException("Parcelamento inválido. Use de 1 a 120 parcelas.");
            // Revalida quantidade, preço e soma: o total recebido de um backup não é considerado confiável.
            if (e.Items is null || e.Items.Count > 500) throw new BudgetException("A lista permite até 500 itens.");
            foreach (var item in e.Items)
            {
                if (item is null) throw new BudgetException("Item inválido.");
                Text(item.Name, "Nome do item", 100);
                if (item.Quantity <= 0 || item.Quantity > 100_000 || decimal.Round(item.Quantity, 3) != item.Quantity) throw new BudgetException("Quantidade deve estar entre 0,001 e 100.000, com até três casas decimais.");
                Money.Validate(item.UnitPrice, "Preço unitário", true);
                Money.Validate(item.Total, "Total do item", true);
            }
            if (e.Items.Count > 0 && (e.InstallmentCount > 1 || e.Items.Sum(i => i.Total) != e.Amount))
                throw new BudgetException("O total da despesa precisa ser igual à soma dos itens. Listas devem ser salvas à vista.");
            // Quem pagou só existe em despesas pagas e precisa ser uma pessoa cadastrada.
            if (e.PaidBy is { } payer && (!e.Paid || !ids.Contains(payer))) throw new BudgetException("Quem pagou: escolha uma pessoa cadastrada em uma despesa já paga.");
            // Vínculo com recorrência: id e mês andam juntos, o modelo existe, o mês é exclusivo e o lançamento é simples.
            if ((e.RecurrenceId is null) != (e.RecurrenceMonth is null)) throw new BudgetException("Vínculo com a recorrência incompleto.");
            if (e.RecurrenceId is { } recurrence && (!recurrenceIds.Contains(recurrence) || e.RecurrenceMonth!.Value.Day != 1 || !occurrences.Add((recurrence, e.RecurrenceMonth.Value)) || e.InstallmentCount > 1 || e.Items.Count > 0))
                throw new BudgetException("Despesa recorrente inválida: não pode ter parcelas nem lista de itens, e cada mês aparece uma única vez.");
        }
    }
    /// <summary>
    /// Confere modo de divisão e responsáveis (existentes, sem repetição, um só no modo individual), nas despesas e nas recorrências.
    /// </summary>
    private static void Sharing(SplitMode split, List<Guid>? participants, HashSet<Guid> people, string message = "Selecione as pessoas responsáveis pela recorrência.")
    {
        if (!Enum.IsDefined(split) || participants is null || participants.Count == 0 || participants.Count != participants.Distinct().Count() || participants.Any(id => !people.Contains(id))) throw new BudgetException(message);
        if (split == SplitMode.Individual && participants.Count != 1) throw new BudgetException("Uma despesa individual deve ter exatamente uma pessoa.");
    }
    /// <summary>
    /// Rejeita texto vazio, acima do limite ou com caracteres de controle, como quebras de linha.
    /// </summary>
    public static void Text(string? text, string label, int maximum)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximum || text.Any(char.IsControl))
            throw new BudgetException($"{label}: preencha entre 1 e {maximum} caracteres, sem caracteres de controle.");
    }
    /// <summary>
    /// Restringe os registros ao intervalo de anos suportado pelo domínio, de 2000 a 2100.
    /// </summary>
    public static void Date(DateOnly date)
    {
        if (date.Year is < 2000 or > 2100) throw new BudgetException("Use datas entre os anos 2000 e 2100.");
    }
    /// <summary>
    /// Distribui o valor total de uma compra entre vencimentos mensais, sem perder ou criar centavos.
    /// </summary>
    /// <param name="original">Modelo da compra com valor total em reais, já validado como dinheiro com duas casas.</param>
    /// <param name="count">Número de parcelas, entre 1 e 120.</param>
    /// <returns>Novos lançamentos com identificadores próprios, sem modificar o modelo recebido.</returns>
    public static List<Expense> Installments(Expense original, int count)
    {
        if (count is < 1 or > 120) throw new BudgetException("Use de 1 a 120 parcelas.");
        if (original.Items.Count > 0 && count > 1) throw new BudgetException("Listas de itens devem ser salvas à vista.");
        // Trabalhar em centavos inteiros permite distribuir o resto pelas primeiras parcelas.
        var cents = decimal.ToInt64(original.Amount * 100);
        if (cents < count) throw new BudgetException("Cada parcela precisa ter pelo menos R$ 0,01.");
        var group = count > 1 ? Guid.NewGuid() : (Guid?)null;
        // AddMonths parte sempre da data original: 31/jan → último dia de fevereiro → 31/mar.
        // O grupo relaciona parcelas; as listas são copiadas para não compartilhar suas coleções mutáveis.
        return Enumerable.Range(0, count).Select(i => original with
        {
            Id = Guid.NewGuid(), Amount = (cents / count + (i < cents % count ? 1 : 0)) / 100m,
            DueDate = original.DueDate.AddMonths(i), InstallmentGroup = group,
            InstallmentNumber = i + 1, InstallmentCount = count,
            Paid = count == 1 && original.Paid, Participants = [..original.Participants], Items = [..original.Items]
        }).ToList();
    }
}
