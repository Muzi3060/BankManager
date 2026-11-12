using System.Text;

namespace BankManager.Nucleo;

/// <summary>
/// Exporta as despesas para uma planilha CSV pronta para abrir no Excel em português: ponto e vírgula como separador,
/// vírgula decimal, datas dd/MM/yyyy e UTF-8 com marcador de ordem de bytes (para os acentos aparecerem certos).
/// </summary>
public static class ExportacaoCsv
{
    /// <summary>Cabeçalho das colunas, na ordem em que são escritas.</summary>
    public static readonly string[] Colunas = ["Data", "Descrição", "Categoria", "Valor", "Situação", "Pago por", "Responsáveis", "Divisão", "Parcela", "Recorrente", "Pago em"];
    /// <summary>
    /// Monta o CSV de todas as despesas (ou só as do mês, se informado), da mais antiga à mais recente.
    /// </summary>
    public static string Despesas(DadosDoOrcamento dados, DateOnly? mes = null)
    {
        var nomes = dados.Pessoas.ToDictionary(p => p.Id, p => p.Nome); var br = Dinheiro.Cultura;
        var construtor = new StringBuilder(); construtor.Append(string.Join(';', Colunas.Select(c => Csv.Celula(c)))).Append("\r\n");
        foreach (var e in dados.Despesas.Where(e => mes is null || Datas.Mes(e.Vencimento) == Datas.Mes(mes.Value)).OrderBy(e => e.Vencimento).ThenBy(e => e.Titulo))
        {
            var cotas = string.Join(" / ", MotorFinanceiro.Ratear(dados, e).Valores.Select(a => $"{nomes.GetValueOrDefault(a.Key, "—")} {a.Value.ToString("0.00", br)}"));
            string[] celulas =
            [
                e.Vencimento.ToString("dd/MM/yyyy"), e.Titulo, e.Categoria, e.Valor.ToString("0.00", br), e.Paga ? "Pago" : "Em aberto", e.PagaPor is { } p ? nomes.GetValueOrDefault(p, "—") : "",
                string.Join(" / ", e.Participantes.Select(id => nomes.GetValueOrDefault(id, "—"))), cotas, e.TotalDeParcelas > 1 ? $"{e.NumeroDaParcela}/{e.TotalDeParcelas}" : "", e.IdDaRecorrencia is null ? "" : "Sim",
                e.DataDoPagamento?.ToString("dd/MM/yyyy") ?? ""
            ];
            construtor.Append(string.Join(';', celulas.Select(c => Csv.Celula(c)))).Append("\r\n");
        }
        return construtor.ToString();
    }
    /// <summary>
    /// Grava o texto em UTF-8 com BOM, usando um arquivo temporário e a troca final, para nunca deixar um arquivo pela metade.
    /// </summary>
    public static void Salvar(string caminho, string conteudo)
    {
        var temporario = caminho + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporario, conteudo, new UTF8Encoding(true)); File.Move(temporario, caminho, true); }
        finally { if (File.Exists(temporario)) File.Delete(temporario); }
    }
}
