using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BankManager.Core;

public sealed class LocalStore : IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, MaxDepth = 32, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "financas.json");
    public string BackupPath => Path.Combine(DirectoryPath, "financas.backup.json");
    private FileStream? lease;
    public LocalStore(string? directory = null)
    {
        DirectoryPath = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BankManager");
        Directory.CreateDirectory(DirectoryPath);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(DirectoryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try { lease = new FileStream(Path.Combine(DirectoryPath, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new BudgetException("Não foi possível abrir os dados. Feche outra janela do BankManager e tente novamente."); }
    }
    public BudgetData Load() => File.Exists(FilePath) ? Read(FilePath) : new();
    public static BudgetData Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 20 * 1024 * 1024) throw new BudgetException("O arquivo deve ter no máximo 20 MB.");
        BudgetData data;
        try { data = JsonSerializer.Deserialize<BudgetData>(stream, JsonOptions) ?? throw new BudgetException("Arquivo vazio."); }
        catch (JsonException) { throw new BudgetException("O arquivo não é um backup válido do BankManager. Os dados atuais foram preservados."); }
        BudgetRules.Validate(data);
        return data;
    }
    public void Save(BudgetData data)
    {
        BudgetRules.Validate(data);
        AtomicWrite(FilePath, JsonSerializer.Serialize(data, JsonOptions), File.Exists(FilePath) ? BackupPath : null);
    }
    public static void Export(string path, BudgetData data)
    {
        BudgetRules.Validate(data);
        AtomicWrite(path, JsonSerializer.Serialize(data, JsonOptions));
    }
    private static void AtomicWrite(string path, string content, string? backup = null)
    {
        if (Encoding.UTF8.GetByteCount(content) > 20 * 1024 * 1024) throw new BudgetException("O conjunto de dados excede o limite de 20 MB. Nenhuma alteração foi salva.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.Write(content); writer.Flush(); stream.Flush(true);
            }
            if (backup is not null) File.Copy(path, backup, true);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static BudgetData Clone(BudgetData data) => JsonSerializer.Deserialize<BudgetData>(JsonSerializer.Serialize(data, JsonOptions), JsonOptions)!;
    public void Dispose() => lease?.Dispose();
}
