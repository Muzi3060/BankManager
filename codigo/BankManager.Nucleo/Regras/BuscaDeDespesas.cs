using System.Globalization;
using System.Text;

namespace BankManager.Nucleo;

/// <summary>
/// Busca e filtros da lista de despesas, para um mês ou para todos. Não altera os dados.
/// </summary>
public static class BuscaDeDespesas
{
    /// <summary>
    /// Filtra as despesas. O texto procura no título e na categoria, sem diferenciar maiúsculas, minúsculas nem acentos
    /// ("saude" encontra "Saúde"). Com <paramref name="mes"/>, devolve só aquele mês, por vencimento crescente; sem ele, devolve
    /// todos os meses, do vencimento mais recente ao mais antigo.
    /// </summary>
    /// <param name="dados">Dados a consultar.</param>
    /// <param name="mes">Mês (qualquer dia dele) ou nulo para todos os meses.</param>
    /// <param name="texto">Texto procurado; vazio não filtra.</param>
    /// <param name="paga">Verdadeiro só pagas, falso só em aberto, nulo todas.</param>
    /// <param name="categoria">Categoria exata ou nulo para todas.</param>
    public static List<Despesa> Buscar(DadosDoOrcamento dados, DateOnly? mes, string? texto, bool? paga = null, string? categoria = null)
    {
        var procurado = Simplificar(texto ?? "");
        var consulta = dados.Despesas.Where(e => (mes is null || Datas.Mes(e.Vencimento) == Datas.Mes(mes.Value))
            && (paga is null || e.Paga == paga) && (categoria is null || e.Categoria == categoria)
            && (procurado.Length == 0 || Simplificar(e.Titulo).Contains(procurado, StringComparison.Ordinal) || Simplificar(e.Categoria).Contains(procurado, StringComparison.Ordinal)));
        return (mes is null ? consulta.OrderByDescending(e => e.Vencimento).ThenBy(e => e.Titulo) : consulta.OrderBy(e => e.Vencimento).ThenBy(e => e.Titulo)).ToList();
    }
    /// <summary>
    /// Normaliza para comparação: minúsculas e sem acentos.
    /// </summary>
    public static string Simplificar(string texto)
    {
        var construtor = new StringBuilder(texto.Length);
        foreach (var c in texto.Normalize(NormalizationForm.FormD)) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) construtor.Append(char.ToLowerInvariant(c));
        return construtor.ToString();
    }
}
