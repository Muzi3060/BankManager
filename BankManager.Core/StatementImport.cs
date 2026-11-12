using System.Globalization;
using System.Text;

namespace BankManager.Core;

/// <summary>Quais valores do extrato são despesas.</summary>
public enum AmountSign
{
    /// <summary>Saídas são negativas (extrato de conta corrente). Valores positivos são entradas e são ignorados.</summary>
    NegativeIsExpense,
    /// <summary>Compras são positivas (fatura de cartão). Valores negativos (estornos, pagamentos) são ignorados.</summary>
    PositiveIsExpense
}
/// <summary>
/// Como ler o CSV: o índice (a partir de zero) de cada coluna, se a primeira linha é cabeçalho e o sinal das despesas.
/// </summary>
public sealed record ImportMapping(int DateColumn, int DescriptionColumn, int AmountColumn, bool HasHeader, AmountSign Sign);
/// <summary>
/// Resultado do planejamento: as despesas a criar e o que foi deixado de fora, para a pessoa conferir antes de confirmar.
/// </summary>
/// <param name="Expenses">Despesas novas, prontas para gravar (nada foi gravado ainda).</param>
/// <param name="IgnoredEntries">Linhas ignoradas por não serem despesas (entradas, estornos ou valor zero).</param>
/// <param name="Duplicates">Linhas ignoradas por já existirem (mesma data, valor e descrição), o que permite importar o mesmo extrato duas vezes sem repetir.</param>
/// <param name="Errors">Mensagens das linhas com data ou valor ilegível, com o número da linha no arquivo (as primeiras 20).</param>
/// <param name="ErrorCount">Total de linhas com erro.</param>
public sealed record ImportPlan(List<Expense> Expenses, int IgnoredEntries, int Duplicates, List<string> Errors, int ErrorCount);
/// <summary>
/// Importa um extrato bancário em CSV como despesas. Cada linha vira uma despesa já paga, na categoria "Outros", sem "quem pagou"
/// (assim não entra no acerto de contas). Não grava nada: devolve um plano que a interface confirma e grava.
/// </summary>
public static class StatementImport
{
    private static readonly string[] DateFormats = ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy", "yyyy/MM/dd"];
    /// <summary>Tamanho máximo, em bytes, do arquivo de extrato.</summary>
    public const int MaxBytes = 5 * 1024 * 1024;
    /// <summary>
    /// Lê o arquivo como texto. Tenta UTF-8 e, se houver bytes inválidos, usa Latin-1 (comum em exportações de bancos brasileiros).
    /// </summary>
    public static string ReadFile(string path)
    {
        var info = new FileInfo(path); if (info.Length > MaxBytes) throw new BudgetException("O arquivo deve ter no máximo 5 MB.");
        var bytes = File.ReadAllBytes(path);
        try { return new UTF8Encoding(false, true).GetString(bytes); } catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }
    /// <summary>
    /// Sugere as colunas e se há cabeçalho. Usa os nomes do cabeçalho (data, descrição/histórico, valor) e, na falta deles,
    /// olha o conteúdo: a coluna de datas, a de valores e a primeira de texto.
    /// </summary>
    public static ImportMapping Guess(CsvTable table)
    {
        var first = table.Rows[0]; var width = table.Rows.Max(r => r.Count);
        // É cabeçalho se nenhuma célula da primeira linha for uma data legível.
        var hasHeader = !first.Any(cell => TryParseDate(cell, out _));
        int Find(params string[] words) => hasHeader ? first.FindIndex(h => words.Any(w => ExpenseSearch.Fold(h).Contains(w))) : -1;
        var sample = table.Rows.Skip(hasHeader ? 1 : 0).Take(30).ToList();
        // -1 quando nenhuma coluna livre tem, na maioria das linhas de amostra, valores que passam no teste.
        int ByContent(Func<string, bool> test, params int[] avoid) => Enumerable.Range(0, width).Where(c => !avoid.Contains(c) && sample.Count(r => c < r.Count && test(r[c])) * 2 > sample.Count).DefaultIfEmpty(-1).First();
        var date = Find("data", "date"); if (date < 0) date = ByContent(v => TryParseDate(v, out _));
        var amount = Find("valor", "amount", "quantia"); if (amount < 0) amount = ByContent(v => TryParseAmount(v, out _), date);
        var description = Find("descri", "historico", "memo", "lancamento", "estabelecimento", "title", "nome"); if (description < 0) description = ByContent(v => v.Length > 0 && !TryParseAmount(v, out _) && !TryParseDate(v, out _), date, amount);
        // Valores desconhecidos viram a primeira coluna livre, para a lista de escolha da interface nunca começar vazia.
        int Fallback(int index, params int[] taken) => index >= 0 ? index : Enumerable.Range(0, width).FirstOrDefault(c => !taken.Contains(c));
        date = Fallback(date); amount = Fallback(amount, date); description = Fallback(description, date, amount);
        return new ImportMapping(date, description, amount, hasHeader, AmountSign.NegativeIsExpense);
    }
    /// <summary>
    /// Planeja a importação. As linhas ilegíveis não impedem as outras; elas são listadas para correção.
    /// </summary>
    /// <param name="data">Dados atuais, usados para reconhecer despesas que já existem.</param>
    /// <param name="table">CSV lido.</param>
    /// <param name="mapping">Colunas e sinal escolhidos.</param>
    /// <param name="split">Como dividir cada despesa importada.</param>
    /// <param name="participants">Responsáveis de cada despesa; no modo individual, exatamente um.</param>
    public static ImportPlan Plan(BudgetData data, CsvTable table, ImportMapping mapping, SplitMode split, List<Guid> participants)
    {
        if (participants.Count == 0 || (split == SplitMode.Individual && participants.Count != 1) || participants.Any(id => data.People.All(p => p.Id != id))) throw new BudgetException("Escolha quem é o responsável pelas despesas importadas.");
        // Despesas existentes viram uma contagem (data, valor, descrição): cada uma "consome" no máximo uma linha do arquivo.
        var existing = new Dictionary<(DateOnly, decimal, string), int>();
        foreach (var e in data.Expenses) { var key = (e.DueDate, e.Amount, ExpenseSearch.Fold(e.Title)); existing[key] = existing.GetValueOrDefault(key) + 1; }
        var expenses = new List<Expense>(); var errors = new List<string>(); int ignored = 0, duplicates = 0, errorCount = 0;
        void Fail(int line, string reason) { errorCount++; if (errors.Count < 20) errors.Add($"Linha {line}: {reason}"); }
        for (var i = mapping.HasHeader ? 1 : 0; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i]; var line = i + 1; string Cell(int c) => c >= 0 && c < row.Count ? row[c] : "";
            if (!TryParseDate(Cell(mapping.DateColumn), out var date)) { Fail(line, "data ilegível"); continue; }
            if (date.Year is < 2000 or > 2100) { Fail(line, "data fora de 2000 a 2100"); continue; }
            if (!TryParseAmount(Cell(mapping.AmountColumn), out var value)) { Fail(line, "valor ilegível"); continue; }
            var isExpense = mapping.Sign == AmountSign.NegativeIsExpense ? value < 0 : value > 0;
            if (!isExpense) { ignored++; continue; }
            var amount = Math.Abs(value);
            if (amount > 999_999_999.99m) { Fail(line, "valor grande demais"); continue; }
            var title = Title(Cell(mapping.DescriptionColumn));
            var key = (date, amount, ExpenseSearch.Fold(title));
            if (existing.TryGetValue(key, out var count) && count > 0) { existing[key] = count - 1; duplicates++; continue; }
            expenses.Add(new Expense { Title = title, Category = "Outros", DueDate = date, Amount = amount, Paid = true, PaidDate = date, Split = split, Participants = [.. participants] });
        }
        return new ImportPlan(expenses, ignored, duplicates, errors, errorCount);
    }
    /// <summary>
    /// Limpa uma descrição: tira caracteres de controle, junta espaços repetidos e corta em 120 caracteres; vazia vira "Sem descrição".
    /// </summary>
    private static string Title(string raw)
    {
        var clean = string.Join(' ', new string(raw.Where(c => !char.IsControl(c) || char.IsWhiteSpace(c)).ToArray()).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length == 0 ? "Sem descrição" : clean.Length > 120 ? clean[..120].TrimEnd() : clean;
    }
    /// <summary>
    /// Lê uma data em formatos comuns (dd/MM/aaaa, aaaa-MM-dd, dd-MM-aaaa...). Ignora uma hora depois da data.
    /// </summary>
    public static bool TryParseDate(string? text, out DateOnly date)
    {
        date = default; var s = text?.Trim() ?? ""; if (s.Length == 0) return false;
        var space = s.IndexOfAny([' ', 'T']); if (space > 0) s = s[..space];
        return DateOnly.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
    /// <summary>
    /// Lê um valor em reais nos formatos de extratos: "1.234,56", "1234,56", "-1234.56", "R$ 12,00", "(12,00)" e "12,00-".
    /// Com ponto e vírgula juntos, o último é o separador decimal. Rejeita mais de duas casas decimais e qualquer outro texto.
    /// </summary>
    public static bool TryParseAmount(string? text, out decimal value)
    {
        value = 0; var s = (text ?? "").Trim(); var negative = false;
        if (s.Length >= 2 && s[0] == '(' && s[^1] == ')') { negative = true; s = s[1..^1]; }
        s = s.Replace("R$", "", StringComparison.OrdinalIgnoreCase).Replace("BRL", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "").Replace(" ", "");
        if (s.StartsWith('-')) { negative = !negative; s = s[1..]; } else if (s.StartsWith('+')) s = s[1..];
        if (s.EndsWith('-')) { negative = !negative; s = s[..^1]; }
        if (s.Length == 0 || !char.IsDigit(s[0]) || !s.All(c => char.IsDigit(c) || c is '.' or ',') || !char.IsDigit(s[^1])) return false;
        var lastDot = s.LastIndexOf('.'); var lastComma = s.LastIndexOf(',');
        // O separador decimal é o último símbolo, se houver os dois; se houver um só, ele é decimal quando aparece uma vez e seguido de 1 ou 2 dígitos.
        char? decimalSeparator = lastDot >= 0 && lastComma >= 0 ? (lastDot > lastComma ? '.' : ',') : null;
        if (decimalSeparator is null)
        {
            var only = lastDot >= 0 ? '.' : lastComma >= 0 ? ',' : (char?)null;
            if (only is { } sep && s.Count(c => c == sep) == 1 && s.Length - s.IndexOf(sep) - 1 is 1 or 2) decimalSeparator = sep;
        }
        // Separa parte inteira e decimal. O outro símbolo só pode ser separador de milhar, em grupos de exatamente 3 dígitos
        // ("1.234.567"); qualquer outra mistura ("1,234567", "1.2.3,4") é um valor malformado e é recusada.
        string integer = s, fraction = "";
        if (decimalSeparator is { } ds) { var cut = s.LastIndexOf(ds); integer = s[..cut]; fraction = s[(cut + 1)..]; }
        var thousands = integer.Where(c => c is '.' or ',').Distinct().ToList();
        if (thousands.Count > 1 || fraction.Any(c => c is '.' or ',')) return false;
        if (thousands.Count == 1) { var groups = integer.Split(thousands[0]); if (groups[0].Length is < 1 or > 3 || groups.Skip(1).Any(g => g.Length != 3)) return false; }
        var normalized = integer.Replace(".", "").Replace(",", "") + (fraction.Length > 0 ? "." + fraction : "");
        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) || decimal.Round(parsed, 2) != parsed) return false;
        value = negative ? -parsed : parsed; return true;
    }
}
