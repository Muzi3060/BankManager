using System.Text;

namespace BankManager.Nucleo;

/// <summary>
/// Tabela lida de um CSV: o separador detectado e as linhas, cada uma com suas colunas.
/// </summary>
/// <param name="Delimitador">Separador de colunas (ponto e vírgula, vírgula ou tabulação).</param>
/// <param name="Linhas">Linhas não vazias, na ordem do arquivo. A primeira pode ser um cabeçalho.</param>
public sealed record TabelaCsv(char Delimitador, List<List<string>> Linhas);
/// <summary>
/// Leitura e escrita de CSV (RFC 4180): campos entre aspas, aspas duplicadas e quebras de linha dentro de campos.
/// </summary>
public static class Csv
{
    /// <summary>Limite de tamanho do texto de um CSV importado.</summary>
    public const int MaximoDeCaracteres = 5_000_000;
    /// <summary>Limite de linhas de um CSV importado.</summary>
    public const int MaximoDeLinhas = 10_000;
    /// <summary>
    /// Lê um CSV. O separador é detectado pela primeira linha (ponto e vírgula, vírgula ou tabulação); a linha "sep=;" que o Excel
    /// acrescenta é aceita e ignorada. Linhas totalmente vazias são descartadas.
    /// </summary>
    /// <exception cref="ErroDeOrcamento">Arquivo vazio, grande demais, com linhas demais ou com aspas sem fechar.</exception>
    public static TabelaCsv Interpretar(string texto)
    {
        if (texto.Length > MaximoDeCaracteres) throw new ErroDeOrcamento("O arquivo é grande demais para importar (máximo de 5 milhões de caracteres).");
        texto = texto.TrimStart('﻿');
        var delimitador = ';';
        if (texto.StartsWith("sep=", StringComparison.OrdinalIgnoreCase) && texto.Length > 4) { delimitador = texto[4]; texto = texto[(texto.IndexOfAny(['\n', '\r']) is var corte and >= 0 ? corte : texto.Length)..]; }
        else delimitador = Detectar(texto);
        var fileiras = new List<List<string>>(); var fileira = new List<string>(); var campo = new StringBuilder(); var entreAspas = false; var estavaEntreAspas = false;
        void FecharCampo() { fileira.Add(estavaEntreAspas ? campo.ToString() : campo.ToString().Trim()); campo.Clear(); estavaEntreAspas = false; }
        void FecharLinha()
        {
            FecharCampo();
            if (fileira.Any(f => f.Length > 0)) { fileiras.Add(fileira); if (fileiras.Count > MaximoDeLinhas) throw new ErroDeOrcamento($"O arquivo tem linhas demais (máximo de {MaximoDeLinhas:N0})."); }
            fileira = [];
        }
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (entreAspas)
            {
                if (c == '"') { if (i + 1 < texto.Length && texto[i + 1] == '"') { campo.Append('"'); i++; } else entreAspas = false; }
                else campo.Append(c);
            }
            else if (c == '"' && campo.Length == 0 && !estavaEntreAspas) { entreAspas = true; estavaEntreAspas = true; }
            else if (c == delimitador) FecharCampo();
            else if (c == '\n' || c == '\r') { if (c == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n') i++; FecharLinha(); }
            else campo.Append(c);
        }
        if (entreAspas) throw new ErroDeOrcamento("O arquivo tem aspas sem fechar. Confira se ele é mesmo um CSV.");
        if (campo.Length > 0 || fileira.Count > 0 || estavaEntreAspas) FecharLinha();
        if (fileiras.Count == 0) throw new ErroDeOrcamento("O arquivo está vazio.");
        return new TabelaCsv(delimitador, fileiras);
    }
    /// <summary>
    /// Escolhe o separador que mais aparece, fora de aspas, na primeira linha com conteúdo; em caso de empate, ponto e vírgula.
    /// </summary>
    private static char Detectar(string texto)
    {
        var contagens = new Dictionary<char, int> { [';'] = 0, [','] = 0, ['\t'] = 0 }; var entreAspas = false;
        foreach (var c in texto)
        {
            if (c == '"') entreAspas = !entreAspas;
            else if (!entreAspas && (c == '\n' || c == '\r')) { if (contagens.Values.Sum() > 0) break; }
            else if (!entreAspas && contagens.ContainsKey(c)) contagens[c]++;
        }
        return contagens.OrderByDescending(p => p.Value).ThenBy(p => p.Key == ';' ? 0 : p.Key == ',' ? 1 : 2).First().Key;
    }
    /// <summary>
    /// Prepara um texto para uma célula de CSV. Textos que começam com =, +, -, @, tabulação ou retorno de carro ganham um apóstrofo à
    /// frente: sem isso, o Excel e planilhas semelhantes os executariam como fórmula ("injeção de fórmula"). Campos com separador,
    /// aspas, quebra de linha ou espaços nas pontas vão entre aspas.
    /// </summary>
    public static string Celula(string texto, char delimitador = ';')
    {
        if (texto.Length > 0 && "=+-@\t\r".Contains(texto[0])) texto = "'" + texto;
        return texto.IndexOfAny([delimitador, '"', '\n', '\r']) >= 0 || texto != texto.Trim() ? "\"" + texto.Replace("\"", "\"\"") + "\"" : texto;
    }
}
