using System.Globalization;
using System.Text;

namespace BankManager.Nucleo;

/// <summary>Quais valores do extrato são despesas.</summary>
public enum SinalDoValor
{
    /// <summary>Saídas são negativas (extrato de conta corrente). Valores positivos são entradas e são ignorados.</summary>
    NegativoEDespesa,
    /// <summary>Compras são positivas (fatura de cartão). Valores negativos (estornos, pagamentos) são ignorados.</summary>
    PositivoEDespesa
}
/// <summary>
/// Como ler o CSV: o índice (a partir de zero) de cada coluna, se a primeira linha é cabeçalho e o sinal das despesas.
/// </summary>
public sealed record MapeamentoDeImportacao(int ColunaDaData, int ColunaDaDescricao, int ColunaDoValor, bool TemCabecalho, SinalDoValor Sinal);
/// <summary>
/// Resultado do planejamento: as despesas a criar e o que foi deixado de fora, para a pessoa conferir antes de confirmar.
/// </summary>
/// <param name="Despesas">Despesas novas, prontas para gravar (nada foi gravado ainda).</param>
/// <param name="EntradasIgnoradas">Linhas ignoradas por não serem despesas (entradas, estornos ou valor zero).</param>
/// <param name="Duplicadas">Linhas ignoradas por já existirem (mesma data, valor e descrição), o que permite importar o mesmo extrato duas vezes sem repetir.</param>
/// <param name="Erros">Mensagens das linhas com data ou valor ilegível, com o número da linha no arquivo (as primeiras 20).</param>
/// <param name="TotalDeErros">Total de linhas com erro.</param>
public sealed record PlanoDeImportacao(List<Despesa> Despesas, int EntradasIgnoradas, int Duplicadas, List<string> Erros, int TotalDeErros);
/// <summary>
/// Importa um extrato bancário em CSV como despesas. Cada linha vira uma despesa já paga, na categoria "Outros", sem "quem pagou"
/// (assim não entra no acerto de contas). Não grava nada: devolve um plano que a interface confirma e grava.
/// </summary>
public static class ImportacaoDeExtrato
{
    private static readonly string[] FormatosDeData = ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy", "yyyy/MM/dd"];
    /// <summary>Tamanho máximo, em bytes, do arquivo de extrato.</summary>
    public const int MaximoDeBytes = 5 * 1024 * 1024;
    /// <summary>
    /// Lê o arquivo como texto. Tenta UTF-8 e, se houver bytes inválidos, usa Latin-1 (comum em exportações de bancos brasileiros).
    /// </summary>
    public static string LerArquivo(string caminho)
    {
        var informacao = new FileInfo(caminho); if (informacao.Length > MaximoDeBytes) throw new ErroDeOrcamento("O arquivo deve ter no máximo 5 MB.");
        var bytes = File.ReadAllBytes(caminho);
        try { return new UTF8Encoding(false, true).GetString(bytes); } catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }
    /// <summary>
    /// Sugere as colunas e se há cabeçalho. Usa os nomes do cabeçalho (data, descrição/histórico, valor) e, na falta deles,
    /// olha o conteúdo: a coluna de datas, a de valores e a primeira de texto.
    /// </summary>
    public static MapeamentoDeImportacao Adivinhar(TabelaCsv tabela)
    {
        var primeiro = tabela.Linhas[0]; var largura = tabela.Linhas.Max(r => r.Count);
        // É cabeçalho se nenhuma célula da primeira linha for uma data legível.
        var temCabecalho = !primeiro.Any(celula => TentarInterpretarData(celula, out _));
        int Achar(params string[] palavras) => temCabecalho ? primeiro.FindIndex(h => palavras.Any(w => BuscaDeDespesas.Simplificar(h).Contains(w))) : -1;
        var amostra = tabela.Linhas.Skip(temCabecalho ? 1 : 0).Take(30).ToList();
        // -1 quando nenhuma coluna livre tem, na maioria das linhas de amostra, valores que passam no teste.
        int PeloConteudo(Func<string, bool> teste, params int[] evitar) => Enumerable.Range(0, largura).Where(c => !evitar.Contains(c) && amostra.Count(r => c < r.Count && teste(r[c])) * 2 > amostra.Count).DefaultIfEmpty(-1).First();
        var data = Achar("data", "date"); if (data < 0) data = PeloConteudo(v => TentarInterpretarData(v, out _));
        var valor = Achar("valor", "amount", "quantia"); if (valor < 0) valor = PeloConteudo(v => TentarInterpretarValor(v, out _), data);
        var descricao = Achar("descri", "historico", "memo", "lancamento", "estabelecimento", "title", "nome"); if (descricao < 0) descricao = PeloConteudo(v => v.Length > 0 && !TentarInterpretarValor(v, out _) && !TentarInterpretarData(v, out _), data, valor);
        // Valores desconhecidos viram a primeira coluna livre, para a lista de escolha da interface nunca começar vazia.
        int ResponsavelPadrao(int indice, params int[] usados) => indice >= 0 ? indice : Enumerable.Range(0, largura).FirstOrDefault(c => !usados.Contains(c));
        data = ResponsavelPadrao(data); valor = ResponsavelPadrao(valor, data); descricao = ResponsavelPadrao(descricao, data, valor);
        return new MapeamentoDeImportacao(data, descricao, valor, temCabecalho, SinalDoValor.NegativoEDespesa);
    }
    /// <summary>
    /// Planeja a importação. As linhas ilegíveis não impedem as outras; elas são listadas para correção.
    /// </summary>
    /// <param name="dados">Dados atuais, usados para reconhecer despesas que já existem.</param>
    /// <param name="tabela">CSV lido.</param>
    /// <param name="mapeamento">Colunas e sinal escolhidos.</param>
    /// <param name="divisao">Como dividir cada despesa importada.</param>
    /// <param name="participantes">Responsáveis de cada despesa; no modo individual, exatamente um.</param>
    public static PlanoDeImportacao Planejar(DadosDoOrcamento dados, TabelaCsv tabela, MapeamentoDeImportacao mapeamento, ModoDeDivisao divisao, List<Guid> participantes)
    {
        if (participantes.Count == 0 || (divisao == ModoDeDivisao.Individual && participantes.Count != 1) || participantes.Any(id => dados.Pessoas.All(p => p.Id != id))) throw new ErroDeOrcamento("Escolha quem é o responsável pelas despesas importadas.");
        // Despesas existentes viram uma contagem (data, valor, descrição): cada uma "consome" no máximo uma linha do arquivo.
        var existente = new Dictionary<(DateOnly, decimal, string), int>();
        foreach (var e in dados.Despesas) { var chave = (e.Vencimento, e.Valor, BuscaDeDespesas.Simplificar(e.Titulo)); existente[chave] = existente.GetValueOrDefault(chave) + 1; }
        var despesas = new List<Despesa>(); var erros = new List<string>(); int ignoradas = 0, duplicadas = 0, totalDeErros = 0;
        void Falhar(int linha, string motivo) { totalDeErros++; if (erros.Count < 20) erros.Add($"Linha {linha}: {motivo}"); }
        for (var i = mapeamento.TemCabecalho ? 1 : 0; i < tabela.Linhas.Count; i++)
        {
            var fileira = tabela.Linhas[i]; var linha = i + 1; string Celula(int c) => c >= 0 && c < fileira.Count ? fileira[c] : "";
            if (!TentarInterpretarData(Celula(mapeamento.ColunaDaData), out var data)) { Falhar(linha, "data ilegível"); continue; }
            if (data.Year is < 2000 or > 2100) { Falhar(linha, "data fora de 2000 a 2100"); continue; }
            if (!TentarInterpretarValor(Celula(mapeamento.ColunaDoValor), out var valorLido)) { Falhar(linha, "valor ilegível"); continue; }
            var eDespesa = mapeamento.Sinal == SinalDoValor.NegativoEDespesa ? valorLido < 0 : valorLido > 0;
            if (!eDespesa) { ignoradas++; continue; }
            var valor = Math.Abs(valorLido);
            if (valor > 999_999_999.99m) { Falhar(linha, "valor grande demais"); continue; }
            var titulo = Titulo(Celula(mapeamento.ColunaDaDescricao));
            var chave = (data, valor, BuscaDeDespesas.Simplificar(titulo));
            if (existente.TryGetValue(chave, out var quantidade) && quantidade > 0) { existente[chave] = quantidade - 1; duplicadas++; continue; }
            despesas.Add(new Despesa { Titulo = titulo, Categoria = "Outros", Vencimento = data, Valor = valor, Paga = true, DataDoPagamento = data, Divisao = divisao, Participantes = [.. participantes] });
        }
        return new PlanoDeImportacao(despesas, ignoradas, duplicadas, erros, totalDeErros);
    }
    /// <summary>
    /// Limpa uma descrição: tira caracteres de controle, junta espaços repetidos e corta em 120 caracteres; vazia vira "Sem descrição".
    /// </summary>
    private static string Titulo(string bruto)
    {
        var limpo = string.Join(' ', new string(bruto.Where(c => !char.IsControl(c) || char.IsWhiteSpace(c)).ToArray()).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return limpo.Length == 0 ? "Sem descrição" : limpo.Length > 120 ? limpo[..120].TrimEnd() : limpo;
    }
    /// <summary>
    /// Lê uma data em formatos comuns (dd/MM/aaaa, aaaa-MM-dd, dd-MM-aaaa...). Ignora uma hora depois da data.
    /// </summary>
    public static bool TentarInterpretarData(string? texto, out DateOnly data)
    {
        data = default; var s = texto?.Trim() ?? ""; if (s.Length == 0) return false;
        var espaco = s.IndexOfAny([' ', 'T']); if (espaco > 0) s = s[..espaco];
        return DateOnly.TryParseExact(s, FormatosDeData, CultureInfo.InvariantCulture, DateTimeStyles.None, out data);
    }
    /// <summary>
    /// Lê um valor em reais nos formatos de extratos: "1.234,56", "1234,56", "-1234.56", "R$ 12,00", "(12,00)" e "12,00-".
    /// Com ponto e vírgula juntos, o último é o separador decimal. Rejeita mais de duas casas decimais e qualquer outro texto.
    /// </summary>
    public static bool TentarInterpretarValor(string? texto, out decimal valorLido)
    {
        valorLido = 0; var s = (texto ?? "").Trim(); var negativo = false;
        if (s.Length >= 2 && s[0] == '(' && s[^1] == ')') { negativo = true; s = s[1..^1]; }
        s = s.Replace("R$", "", StringComparison.OrdinalIgnoreCase).Replace("BRL", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "").Replace(" ", "");
        if (s.StartsWith('-')) { negativo = !negativo; s = s[1..]; } else if (s.StartsWith('+')) s = s[1..];
        if (s.EndsWith('-')) { negativo = !negativo; s = s[..^1]; }
        if (s.Length == 0 || !char.IsDigit(s[0]) || !s.All(c => char.IsDigit(c) || c is '.' or ',') || !char.IsDigit(s[^1])) return false;
        var ultimoPonto = s.LastIndexOf('.'); var ultimaVirgula = s.LastIndexOf(',');
        // O separador decimal é o último símbolo, se houver os dois; se houver um só, ele é decimal quando aparece uma vez e seguido de 1 ou 2 dígitos.
        char? separadorDecimal = ultimoPonto >= 0 && ultimaVirgula >= 0 ? (ultimoPonto > ultimaVirgula ? '.' : ',') : null;
        if (separadorDecimal is null)
        {
            var apenas = ultimoPonto >= 0 ? '.' : ultimaVirgula >= 0 ? ',' : (char?)null;
            if (apenas is { } separador && s.Count(c => c == separador) == 1 && s.Length - s.IndexOf(separador) - 1 is 1 or 2) separadorDecimal = separador;
        }
        // Separa parte inteira e decimal. O outro símbolo só pode ser separador de milhar, em grupos de exatamente 3 dígitos
        // ("1.234.567"); qualquer outra mistura ("1,234567", "1.2.3,4") é um valor malformado e é recusada.
        string inteiro = s, fracao = "";
        if (separadorDecimal is { } ds) { var corte = s.LastIndexOf(ds); inteiro = s[..corte]; fracao = s[(corte + 1)..]; }
        var milhares = inteiro.Where(c => c is '.' or ',').Distinct().ToList();
        if (milhares.Count > 1 || fracao.Any(c => c is '.' or ',')) return false;
        if (milhares.Count == 1) { var grupos = inteiro.Split(milhares[0]); if (grupos[0].Length is < 1 or > 3 || grupos.Skip(1).Any(g => g.Length != 3)) return false; }
        var normalizado = inteiro.Replace(".", "").Replace(",", "") + (fracao.Length > 0 ? "." + fracao : "");
        if (!decimal.TryParse(normalizado, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var interpretadas) || decimal.Round(interpretadas, 2) != interpretadas) return false;
        valorLido = negativo ? -interpretadas : interpretadas; return true;
    }
}
