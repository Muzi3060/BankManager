namespace BankManager.Core;

/// <summary>
/// Uma dívida entre duas pessoas: <paramref name="DebtorId"/> deve <paramref name="Amount"/> a <paramref name="CreditorId"/>.
/// </summary>
/// <param name="DebtorId">Quem deve.</param>
/// <param name="CreditorId">Quem tem a receber.</param>
/// <param name="Amount">Valor líquido, sempre positivo.</param>
public sealed record Debt(Guid DebtorId, Guid CreditorId, decimal Amount);
/// <summary>
/// Calcula "quem deve a quem" a partir de quem pagou cada despesa e dos acertos já registrados. Não altera os dados.
/// </summary>
public static class SettlementEngine
{
    /// <summary>
    /// Saldo acumulado entre cada par de pessoas, somando todos os meses.
    /// </summary>
    /// <remarks>
    /// Regras: só despesas pagas <b>com pagador informado</b> entram. Cada participante diferente do pagador deve ao pagador a sua parte
    /// do rateio (a mesma exibida na lista). Um acerto abate o que o pagador do acerto devia ao recebedor. As dívidas de cada par são
    /// compensadas entre si: se Ana pagou R$ 200 por Bruno e Bruno pagou R$ 120 por Ana, resta Bruno devendo R$ 80. Acertos acima do
    /// devido invertem o sentido. Como o rateio proporcional depende da renda de cada mês, editar uma renda antiga muda o valor devido.
    /// </remarks>
    public static List<Debt> Debts(BudgetData data)
    {
        // owed[(devedor, credor)] = quanto o devedor já "tomou emprestado" do credor, antes de compensar o sentido contrário.
        var owed = new Dictionary<(Guid, Guid), decimal>();
        void Add(Guid debtor, Guid creditor, decimal amount) => owed[(debtor, creditor)] = owed.GetValueOrDefault((debtor, creditor)) + amount;
        foreach (var expense in data.Expenses.Where(e => e.Paid && e.PaidBy is not null))
            foreach (var (person, share) in FinanceEngine.Allocate(data, expense).Amounts.Where(a => a.Key != expense.PaidBy)) Add(person, expense.PaidBy!.Value, share);
        // Quem pagou um acerto reduz a própria dívida com quem recebeu.
        foreach (var settlement in data.Settlements) Add(settlement.PayerId, settlement.ReceiverId, -settlement.Amount);
        var result = new List<Debt>();
        foreach (var (a, b) in owed.Keys.Select(k => k.Item1.CompareTo(k.Item2) < 0 ? k : (k.Item2, k.Item1)).Distinct())
        {
            // net > 0: a deve a b; net < 0: b deve a a.
            var net = owed.GetValueOrDefault((a, b)) - owed.GetValueOrDefault((b, a));
            if (net > 0) result.Add(new Debt(a, b, net)); else if (net < 0) result.Add(new Debt(b, a, -net));
        }
        // Ordem estável: maiores dívidas primeiro, desempate por identificador.
        return result.OrderByDescending(d => d.Amount).ThenBy(d => d.DebtorId).ThenBy(d => d.CreditorId).ToList();
    }
}
