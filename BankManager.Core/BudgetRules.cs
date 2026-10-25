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
    /// Verifica versão, limites, unicidade, referências e coerência financeira, lançando BudgetException na primeira falha.
    /// </summary>
    /// <remarks>Não corrige registros nem altera o orçamento recebido. Campos são verificados mesmo que a interface já os tenha validado.</remarks>
    public static void Validate(BudgetData data)
    {
        if (data.Version != 1) throw new BudgetException("Versão de arquivo não suportada.");
        if (data.People is null || data.Incomes is null || data.Expenses is null || data.People.Count > 200 || data.Incomes.Count > 100_000 || data.Expenses.Count > 100_000)
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
        // A chave composta impede duas rendas para a mesma pessoa no mesmo mês.
        var months = new HashSet<(Guid, DateOnly)>();
        foreach (var income in data.Incomes)
        {
            if (income is null || !ids.Contains(income.PersonId) || income.Month.Day != 1 || !months.Add((income.PersonId, income.Month)))
                throw new BudgetException("Registro de renda inválido ou repetido.");
            Date(income.Month);
            Money.Validate(income.Amount, "Renda");
        }
        // Cada lançamento é validado individualmente; parcelas podem ser excluídas ou editadas separadamente.
        var expenseIds = new HashSet<Guid>();
        foreach (var e in data.Expenses)
        {
            if (e is null || e.Id == Guid.Empty || !expenseIds.Add(e.Id)) throw new BudgetException("Identificador de despesa inválido ou repetido.");
            Text(e.Title, "Descrição", 120);
            if (!Categories.Contains(e.Category)) throw new BudgetException("Categoria inválida.");
            Date(e.DueDate);
            Money.Validate(e.Amount, "Valor da despesa", true);
            // Referências inexistentes ou duplicadas causariam rateios ambíguos; o modo também precisa ser conhecido.
            if (!Enum.IsDefined(e.Split) || e.Participants is null || e.Participants.Count == 0 || e.Participants.Count != e.Participants.Distinct().Count() || e.Participants.Any(id => !ids.Contains(id)))
                throw new BudgetException("Selecione as pessoas responsáveis pela despesa.");
            if (e.Split == SplitMode.Individual && e.Participants.Count != 1) throw new BudgetException("Uma despesa individual deve ter exatamente uma pessoa.");
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
        }
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
