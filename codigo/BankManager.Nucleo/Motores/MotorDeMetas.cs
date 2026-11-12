namespace BankManager.Nucleo;

/// <summary>
/// Quanto de uma meta já foi usado. <paramref name="Fracao"/> é gasto dividido pela meta (1 = 100%).
/// </summary>
/// <param name="Categoria">Categoria da meta.</param>
/// <param name="Gasto">Total das despesas do mês na categoria, pagas ou em aberto.</param>
/// <param name="Meta">Limite mensal.</param>
/// <param name="Fracao">Gasto dividido pela meta; pode passar de 1.</param>
public sealed record ProgressoDaMeta(string Categoria, decimal Gasto, decimal Meta, decimal Fracao)
{
    /// <summary>A partir de que fração da meta o aviso "perto do limite" aparece.</summary>
    public const decimal FracaoDePerto = 0.8m;
    /// <summary>Situação da meta: dentro do limite, perto dele (80% a 100%) ou acima.</summary>
    public NivelDaMeta Nivel => Fracao > 1 ? NivelDaMeta.Acima : Fracao >= FracaoDePerto ? NivelDaMeta.Perto : NivelDaMeta.Dentro;
}
/// <summary>
/// Situação de uma meta em um mês.
/// </summary>
public enum NivelDaMeta
{
    /// <summary>Abaixo de 80% da meta.</summary>
    Dentro,
    /// <summary>Entre 80% e 100% da meta, inclusive.</summary>
    Perto,
    /// <summary>Acima da meta.</summary>
    Acima
}
/// <summary>
/// Define e acompanha metas mensais por categoria. Trabalha sobre os dados recebidos e não acessa disco.
/// </summary>
public static class MotorDeMetas
{
    /// <summary>
    /// Define (ou troca) a meta de uma categoria existente.
    /// </summary>
    public static void Definir(DadosDoOrcamento dados, string categoria, decimal valor)
    {
        if (!RegrasDoOrcamento.TodasAsCategorias(dados).Contains(categoria)) throw new ErroDeOrcamento("Categoria inválida.");
        Dinheiro.Validar(valor, "Meta", true);
        var meta = dados.Metas.FirstOrDefault(g => g.Categoria == categoria);
        if (meta is null) dados.Metas.Add(new MetaDeCategoria { Categoria = categoria, Valor = valor }); else meta.Valor = valor;
    }
    /// <summary>
    /// Remove a meta da categoria, se houver.
    /// </summary>
    public static void Limpar(DadosDoOrcamento dados, string categoria) => dados.Metas.RemoveAll(g => g.Categoria == categoria);
    /// <summary>
    /// Progresso de todas as metas no mês, na ordem das categorias; categorias sem gasto aparecem com gasto zero.
    /// </summary>
    public static List<ProgressoDaMeta> Progresso(DadosDoOrcamento dados, DateOnly mes)
    {
        var gasto = MotorFinanceiro.Despesas(dados, mes).GroupBy(e => e.Categoria).ToDictionary(g => g.Key, g => g.Sum(e => e.Valor));
        var ordem = RegrasDoOrcamento.TodasAsCategorias(dados);
        return dados.Metas.OrderBy(g => ordem.IndexOf(g.Categoria)).Select(g => new ProgressoDaMeta(g.Categoria, gasto.GetValueOrDefault(g.Categoria), g.Valor, gasto.GetValueOrDefault(g.Categoria) / g.Valor)).ToList();
    }
}
