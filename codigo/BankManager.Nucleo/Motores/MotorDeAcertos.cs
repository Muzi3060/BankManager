namespace BankManager.Nucleo;

/// <summary>
/// Uma dívida entre duas pessoas: <paramref name="IdDoDevedor"/> deve <paramref name="Valor"/> a <paramref name="IdDoCredor"/>.
/// </summary>
/// <param name="IdDoDevedor">Quem deve.</param>
/// <param name="IdDoCredor">Quem tem a receber.</param>
/// <param name="Valor">Valor líquido, sempre positivo.</param>
public sealed record Divida(Guid IdDoDevedor, Guid IdDoCredor, decimal Valor);
/// <summary>
/// Calcula "quem deve a quem" a partir de quem pagou cada despesa e dos acertos já registrados. Não altera os dados.
/// </summary>
public static class MotorDeAcertos
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
    public static List<Divida> Dividas(DadosDoOrcamento dados)
    {
        // owed[(devedor, credor)] = quanto o devedor já "tomou emprestado" do credor, antes de compensar o sentido contrário.
        var devido = new Dictionary<(Guid, Guid), decimal>();
        void Adicionar(Guid devedor, Guid credor, decimal valor) => devido[(devedor, credor)] = devido.GetValueOrDefault((devedor, credor)) + valor;
        foreach (var despesa in dados.Despesas.Where(e => e.Paga && e.PagaPor is not null))
            foreach (var (pessoa, compartilhar) in MotorFinanceiro.Ratear(dados, despesa).Valores.Where(a => a.Key != despesa.PagaPor)) Adicionar(pessoa, despesa.PagaPor!.Value, compartilhar);
        // Quem pagou um acerto reduz a própria dívida com quem recebeu.
        foreach (var acerto in dados.Acertos) Adicionar(acerto.IdDoPagador, acerto.IdDoRecebedor, -acerto.Valor);
        var resultado = new List<Divida>();
        foreach (var (a, b) in devido.Keys.Select(k => k.Item1.CompareTo(k.Item2) < 0 ? k : (k.Item2, k.Item1)).Distinct())
        {
            // net > 0: a deve a b; net < 0: b deve a a.
            var liquido = devido.GetValueOrDefault((a, b)) - devido.GetValueOrDefault((b, a));
            if (liquido > 0) resultado.Add(new Divida(a, b, liquido)); else if (liquido < 0) resultado.Add(new Divida(b, a, -liquido));
        }
        // Ordem estável: maiores dívidas primeiro, desempate por identificador.
        return resultado.OrderByDescending(d => d.Valor).ThenBy(d => d.IdDoDevedor).ThenBy(d => d.IdDoCredor).ToList();
    }
}
