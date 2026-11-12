using System.Text;

namespace BankManager.Core;

/// <summary>
/// Tabela lida de um CSV: o separador detectado e as linhas, cada uma com suas colunas.
/// </summary>
/// <param name="Delimiter">Separador de colunas (ponto e vírgula, vírgula ou tabulação).</param>
/// <param name="Rows">Linhas não vazias, na ordem do arquivo. A primeira pode ser um cabeçalho.</param>
public sealed record CsvTable(char Delimiter, List<List<string>> Rows);
/// <summary>
/// Leitura e escrita de CSV (RFC 4180): campos entre aspas, aspas duplicadas e quebras de linha dentro de campos.
/// </summary>
public static class Csv
{
    /// <summary>Limite de tamanho do texto de um CSV importado.</summary>
    public const int MaxChars = 5_000_000;
    /// <summary>Limite de linhas de um CSV importado.</summary>
    public const int MaxRows = 10_000;
    /// <summary>
    /// Lê um CSV. O separador é detectado pela primeira linha (ponto e vírgula, vírgula ou tabulação); a linha "sep=;" que o Excel
    /// acrescenta é aceita e ignorada. Linhas totalmente vazias são descartadas.
    /// </summary>
    /// <exception cref="BudgetException">Arquivo vazio, grande demais, com linhas demais ou com aspas sem fechar.</exception>
    public static CsvTable Parse(string text)
    {
        if (text.Length > MaxChars) throw new BudgetException("O arquivo é grande demais para importar (máximo de 5 milhões de caracteres).");
        text = text.TrimStart('﻿');
        var delimiter = ';';
        if (text.StartsWith("sep=", StringComparison.OrdinalIgnoreCase) && text.Length > 4) { delimiter = text[4]; text = text[(text.IndexOfAny(['\n', '\r']) is var cut and >= 0 ? cut : text.Length)..]; }
        else delimiter = Detect(text);
        var rows = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder(); var quoted = false; var wasQuoted = false;
        void EndField() { row.Add(wasQuoted ? field.ToString() : field.ToString().Trim()); field.Clear(); wasQuoted = false; }
        void EndRow()
        {
            EndField();
            if (row.Any(f => f.Length > 0)) { rows.Add(row); if (rows.Count > MaxRows) throw new BudgetException($"O arquivo tem linhas demais (máximo de {MaxRows:N0})."); }
            row = [];
        }
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; } else quoted = false; }
                else field.Append(c);
            }
            else if (c == '"' && field.Length == 0 && !wasQuoted) { quoted = true; wasQuoted = true; }
            else if (c == delimiter) EndField();
            else if (c == '\n' || c == '\r') { if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; EndRow(); }
            else field.Append(c);
        }
        if (quoted) throw new BudgetException("O arquivo tem aspas sem fechar. Confira se ele é mesmo um CSV.");
        if (field.Length > 0 || row.Count > 0 || wasQuoted) EndRow();
        if (rows.Count == 0) throw new BudgetException("O arquivo está vazio.");
        return new CsvTable(delimiter, rows);
    }
    /// <summary>
    /// Escolhe o separador que mais aparece, fora de aspas, na primeira linha com conteúdo; em caso de empate, ponto e vírgula.
    /// </summary>
    private static char Detect(string text)
    {
        var counts = new Dictionary<char, int> { [';'] = 0, [','] = 0, ['\t'] = 0 }; var quoted = false;
        foreach (var c in text)
        {
            if (c == '"') quoted = !quoted;
            else if (!quoted && (c == '\n' || c == '\r')) { if (counts.Values.Sum() > 0) break; }
            else if (!quoted && counts.ContainsKey(c)) counts[c]++;
        }
        return counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key == ';' ? 0 : p.Key == ',' ? 1 : 2).First().Key;
    }
    /// <summary>
    /// Prepara um texto para uma célula de CSV. Textos que começam com =, +, -, @, tabulação ou retorno de carro ganham um apóstrofo à
    /// frente: sem isso, o Excel e planilhas semelhantes os executariam como fórmula ("injeção de fórmula"). Campos com separador,
    /// aspas, quebra de linha ou espaços nas pontas vão entre aspas.
    /// </summary>
    public static string Cell(string text, char delimiter = ';')
    {
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
        return text.IndexOfAny([delimiter, '"', '\n', '\r']) >= 0 || text != text.Trim() ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }
}
