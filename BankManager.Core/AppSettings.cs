using System.Text.Json;
using System.Text.Json.Serialization;

namespace BankManager.Core;

/// <summary>
/// Aparência escolhida por quem usa o aplicativo. "Auto" acompanha o tema do sistema operacional.
/// </summary>
public enum ThemePreference
{
    /// <summary>Segue o tema claro ou escuro definido no sistema.</summary>
    Auto,
    /// <summary>Sempre usa o tema claro.</summary>
    Light,
    /// <summary>Sempre usa o tema escuro.</summary>
    Dark
}
/// <summary>
/// Preferências do aplicativo. Ficam em <c>settings.json</c>, separadas dos dados financeiros,
/// para que mudar a aparência nunca altere nem arrisque o <c>financas.json</c>.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Versão do formato deste arquivo; independente de <see cref="BudgetData.Version"/>.</summary>
    public int Version { get; set; } = 1;
    /// <summary>Tema escolhido; o padrão acompanha o sistema.</summary>
    public ThemePreference Theme { get; set; } = ThemePreference.Auto;
}
/// <summary>
/// Lê e grava <see cref="AppSettings"/> na pasta de dados. Ao contrário das finanças, uma preferência
/// inválida nunca deve impedir o uso do aplicativo: a leitura volta ao padrão em vez de lançar erro.
/// </summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, MaxDepth = 8,
        // Guarda o tema como texto legível ("Dark") e não como número.
        Converters = { new JsonStringEnumConverter() }
    };
    /// <summary>Nome do arquivo de preferências dentro da pasta de dados.</summary>
    public const string FileName = "settings.json";
    /// <summary>
    /// Lê as preferências da pasta indicada. Arquivo ausente, ilegível, grande demais ou com valores
    /// desconhecidos resulta nas preferências padrão.
    /// </summary>
    public static AppSettings Load(string directory)
    {
        try
        {
            var path = Path.Combine(directory, FileName);
            // Um arquivo de preferências nunca precisa de mais que alguns bytes; o limite evita ler lixo.
            if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024) return new();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
            // Valores fora do enum (por exemplo "Roxo" escrito à mão) são tratados como corrompidos.
            return settings is not null && Enum.IsDefined(settings.Theme) ? settings : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { return new(); }
    }
    /// <summary>
    /// Grava as preferências em um temporário e o substitui, para que uma falha no meio nunca deixe
    /// o arquivo pela metade. Erros de disco são propagados ao chamador.
    /// </summary>
    public static void Save(string directory, AppSettings settings)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
