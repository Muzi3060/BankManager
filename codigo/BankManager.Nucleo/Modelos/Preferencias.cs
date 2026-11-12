using System.Text.Json;
using System.Text.Json.Serialization;

namespace BankManager.Nucleo;

/// <summary>
/// Aparência escolhida por quem usa o aplicativo. "Auto" acompanha o tema do sistema operacional.
/// </summary>
public enum PreferenciaDeTema
{
    /// <summary>Segue o tema claro ou escuro definido no sistema.</summary>
    [JsonStringEnumMemberName("Auto")] Automatico,
    /// <summary>Sempre usa o tema claro.</summary>
    [JsonStringEnumMemberName("Light")] Claro,
    /// <summary>Sempre usa o tema escuro.</summary>
    [JsonStringEnumMemberName("Dark")] Escuro
}
/// <summary>
/// Preferências do aplicativo. Ficam em <c>settings.json</c>, separadas dos dados financeiros,
/// para que mudar a aparência nunca altere nem arrisque o <c>financas.json</c>.
/// </summary>
public sealed record Preferencias
{
    /// <summary>Versão do formato deste arquivo; independente de <see cref="DadosDoOrcamento.Versao"/>.</summary>
    [JsonPropertyName("Version")] public int Versao { get; set; } = 1;
    /// <summary>Tema escolhido; o padrão acompanha o sistema.</summary>
    [JsonPropertyName("Theme")] public PreferenciaDeTema Tema { get; set; } = PreferenciaDeTema.Automatico;
}
/// <summary>
/// Lê e grava <see cref="Preferencias"/> na pasta de dados. Ao contrário das finanças, uma preferência
/// inválida nunca deve impedir o uso do aplicativo: a leitura volta ao padrão em vez de lançar erro.
/// </summary>
public static class ArmazenamentoDePreferencias
{
    private static readonly JsonSerializerOptions Opcoes = new()
    {
        WriteIndented = true, MaxDepth = 8,
        // Guarda o tema como texto legível ("Dark") e não como número.
        Converters = { new JsonStringEnumConverter() }
    };
    /// <summary>Nome do arquivo de preferências dentro da pasta de dados.</summary>
    public const string NomeDoArquivo = "settings.json";
    /// <summary>
    /// Lê as preferências da pasta indicada. Arquivo ausente, ilegível, grande demais ou com valores
    /// desconhecidos resulta nas preferências padrão.
    /// </summary>
    public static Preferencias Carregar(string diretorio)
    {
        try
        {
            var caminho = Path.Combine(diretorio, NomeDoArquivo);
            // Um arquivo de preferências nunca precisa de mais que alguns bytes; o limite evita ler lixo.
            if (!File.Exists(caminho) || new FileInfo(caminho).Length > 64 * 1024) return new();
            var preferencias = JsonSerializer.Deserialize<Preferencias>(File.ReadAllText(caminho), Opcoes);
            // Valores fora do enum (por exemplo "Roxo" escrito à mão) são tratados como corrompidos.
            return preferencias is not null && Enum.IsDefined(preferencias.Tema) ? preferencias : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { return new(); }
    }
    /// <summary>
    /// Grava as preferências em um temporário e o substitui, para que uma falha no meio nunca deixe
    /// o arquivo pela metade. Erros de disco são propagados ao chamador.
    /// </summary>
    public static void Salvar(string diretorio, Preferencias preferencias)
    {
        Directory.CreateDirectory(diretorio);
        var caminho = Path.Combine(diretorio, NomeDoArquivo);
        var temporario = caminho + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporario, JsonSerializer.Serialize(preferencias, Opcoes));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporario, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporario, caminho, true);
        }
        finally { if (File.Exists(temporario)) File.Delete(temporario); }
    }
}
