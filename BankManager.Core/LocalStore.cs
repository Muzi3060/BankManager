using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BankManager.Core;

/// <summary>
/// Armazena o orçamento em JSON local, mantém uma versão anterior e coordena acesso entre instâncias cooperantes.
/// </summary>
/// <remarks>Não criptografa os dados. A instância deve ser descartada para liberar o arquivo de bloqueio.</remarks>
public sealed class LocalStore : IDisposable
{
    /// <summary>
    /// Formato legível, com profundidade limitada e rejeição de campos desconhecidos para detectar backups incompatíveis.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, MaxDepth = 32, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    /// <summary>
    /// Pasta efetiva que contém os dados e o bloqueio de acesso.
    /// </summary>
    public string DirectoryPath { get; }
    /// <summary>
    /// Caminho da base atual do aplicativo.
    /// </summary>
    public string FilePath => Path.Combine(DirectoryPath, "financas.json");
    /// <summary>
    /// Caminho da cópia anterior ao último salvamento, não de um histórico ilimitado.
    /// </summary>
    public string BackupPath => Path.Combine(DirectoryPath, "financas.backup.json");
    // Mantém o handle aberto por toda a vida do armazenamento; fechar a janela libera o bloqueio.
    private FileStream? lease;
    /// <summary>
    /// Cria a pasta privada no Unix e mantém um arquivo aberto sem compartilhamento para impedir duas instâncias.
    /// </summary>
    /// <param name="directory">Pasta alternativa para testes; quando nula, utiliza a pasta local de dados do usuário.</param>
    public LocalStore(string? directory = null)
    {
        DirectoryPath = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BankManager");
        Directory.CreateDirectory(DirectoryPath);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(DirectoryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try { lease = new FileStream(Path.Combine(DirectoryPath, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new BudgetException("Não foi possível abrir os dados. Feche outra janela do BankManager e tente novamente."); }
    }
    /// <summary>
    /// Lê a base atual ou retorna um orçamento vazio apenas se o arquivo ainda não existe; erros não apagam dados.
    /// </summary>
    public BudgetData Load() => File.Exists(FilePath) ? Read(FilePath) : new();
    /// <summary>
    /// Lê e valida um JSON com limite de 20 MB; usado igualmente para base, importação e recuperação.
    /// </summary>
    public static BudgetData Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 20 * 1024 * 1024) throw new BudgetException("O arquivo deve ter no máximo 20 MB.");
        BudgetData data;
        try { data = JsonSerializer.Deserialize<BudgetData>(stream, JsonOptions) ?? throw new BudgetException("Arquivo vazio."); }
        catch (JsonException) { throw new BudgetException("O arquivo não é um backup válido do BankManager. Os dados atuais foram preservados."); }
        // A leitura só entrega registros depois de validar as regras de negócio e todas as referências.
        BudgetRules.Validate(data);
        return data;
    }
    /// <summary>
    /// Valida e grava o orçamento atual, preservando a versão existente como backup antes da substituição.
    /// </summary>
    public void Save(BudgetData data)
    {
        BudgetRules.Validate(data);
        AtomicWrite(FilePath, JsonSerializer.Serialize(data, JsonOptions), File.Exists(FilePath) ? BackupPath : null);
    }
    /// <summary>
    /// Valida e exporta uma cópia independente, sem trocar a base ativa nem manter backup do arquivo de destino.
    /// </summary>
    public static void Export(string path, BudgetData data)
    {
        BudgetRules.Validate(data);
        AtomicWrite(path, JsonSerializer.Serialize(data, JsonOptions));
    }
    /// <summary>
    /// Grava em temporário no mesmo diretório, força flush e substitui o destino após o arquivo estar completo.
    /// </summary>
    /// <remarks>Uma falha é propagada ao chamador. O backup é atualizado antes da troca, e o temporário é removido ao sair quando possível.</remarks>
    private static void AtomicWrite(string path, string content, string? backup = null)
    {
        // Aplica na saída o mesmo limite da entrada para não salvar uma base que o próprio aplicativo recusaria abrir.
        if (Encoding.UTF8.GetByteCount(content) > 20 * 1024 * 1024) throw new BudgetException("O conjunto de dados excede o limite de 20 MB. Nenhuma alteração foi salva.");
        // Mesmo diretório mantém a renomeação no mesmo sistema de arquivos; nome único evita colisões.
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                // O writer libera seu buffer antes do Flush(true), que solicita também a descarga do buffer do sistema.
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.Write(content); writer.Flush(); stream.Flush(true);
            }
            // Só substitui o principal depois que a cópia anterior foi preservada com sucesso.
            if (backup is not null) File.Copy(path, backup, true);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    /// <summary>
    /// Cria uma cópia profunda pelo formato JSON, isolando listas e registros durante uma tentativa de edição.
    /// </summary>
    public static BudgetData Clone(BudgetData data) => JsonSerializer.Deserialize<BudgetData>(JsonSerializer.Serialize(data, JsonOptions), JsonOptions)!;
    /// <summary>
    /// Fecha o handle do arquivo de bloqueio para permitir a abertura por outra instância.
    /// </summary>
    public void Dispose() => lease?.Dispose();
}
