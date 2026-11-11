using System.Text;

namespace BankManager.Core;

/// <summary>
/// Exporta as despesas para uma planilha CSV pronta para abrir no Excel em português: ponto e vírgula como separador,
/// vírgula decimal, datas dd/MM/yyyy e UTF-8 com marcador de ordem de bytes (para os acentos aparecerem certos).
/// </summary>
public static class CsvExport
{
    /// <summary>Cabeçalho das colunas, na ordem em que são escritas.</summary>
    public static readonly string[] Columns = ["Data", "Descrição", "Categoria", "Valor", "Situação", "Pago por", "Responsáveis", "Divisão", "Parcela", "Recorrente"];
    /// <summary>
    /// Monta o CSV de todas as despesas (ou só as do mês, se informado), da mais antiga à mais recente.
    /// </summary>
    public static string Expenses(BudgetData data, DateOnly? month = null)
    {
        var names = data.People.ToDictionary(p => p.Id, p => p.Name); var br = Money.Culture;
        var builder = new StringBuilder(); builder.Append(string.Join(';', Columns.Select(c => Csv.Cell(c)))).Append("\r\n");
        foreach (var e in data.Expenses.Where(e => month is null || Dates.Month(e.DueDate) == Dates.Month(month.Value)).OrderBy(e => e.DueDate).ThenBy(e => e.Title))
        {
            var shares = string.Join(" / ", FinanceEngine.Allocate(data, e).Amounts.Select(a => $"{names.GetValueOrDefault(a.Key, "—")} {a.Value.ToString("0.00", br)}"));
            string[] cells =
            [
                e.DueDate.ToString("dd/MM/yyyy"), e.Title, e.Category, e.Amount.ToString("0.00", br), e.Paid ? "Pago" : "Em aberto", e.PaidBy is { } p ? names.GetValueOrDefault(p, "—") : "",
                string.Join(" / ", e.Participants.Select(id => names.GetValueOrDefault(id, "—"))), shares, e.InstallmentCount > 1 ? $"{e.InstallmentNumber}/{e.InstallmentCount}" : "", e.RecurrenceId is null ? "" : "Sim"
            ];
            builder.Append(string.Join(';', cells.Select(c => Csv.Cell(c)))).Append("\r\n");
        }
        return builder.ToString();
    }
    /// <summary>
    /// Grava o texto em UTF-8 com BOM, usando um arquivo temporário e a troca final, para nunca deixar um arquivo pela metade.
    /// </summary>
    public static void Save(string path, string content)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, content, new UTF8Encoding(true)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
