using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BankManager.Nucleo;

/// <summary>
/// Armazena o orçamento em JSON local, mantém uma versão anterior e coordena acesso entre instâncias cooperantes.
/// </summary>
/// <remarks>Não criptografa os dados. A instância deve ser descartada para liberar o arquivo de bloqueio.</remarks>
public sealed class ArmazenamentoLocal : IDisposable
{
    /// <summary>
    /// Formato legível, com profundidade limitada e rejeição de campos desconhecidos para detectar backups incompatíveis.
    /// </summary>
    public static readonly JsonSerializerOptions OpcoesJson = new() { WriteIndented = true, MaxDepth = 32, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    /// <summary>
    /// Pasta efetiva que contém os dados e o bloqueio de acesso.
    /// </summary>
    public string CaminhoDaPasta { get; }
    /// <summary>
    /// Caminho da base atual do aplicativo.
    /// </summary>
    public string CaminhoDoArquivo => Path.Combine(CaminhoDaPasta, "financas.json");
    /// <summary>
    /// Caminho da cópia anterior ao último salvamento, não de um histórico ilimitado.
    /// </summary>
    public string CaminhoDaCopia => Path.Combine(CaminhoDaPasta, "financas.backup.json");
    // Mantém o handle aberto por toda a vida do armazenamento; fechar a janela libera o bloqueio.
    private FileStream? bloqueio;
    /// <summary>
    /// Cria a pasta privada no Unix e mantém um arquivo aberto sem compartilhamento para impedir duas instâncias.
    /// </summary>
    /// <param name="diretorio">Pasta alternativa para testes; quando nula, utiliza a pasta local de dados do usuário.</param>
    public ArmazenamentoLocal(string? diretorio = null)
    {
        CaminhoDaPasta = PastaPadrao(diretorio);
        Directory.CreateDirectory(CaminhoDaPasta);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(CaminhoDaPasta, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try { bloqueio = new FileStream(Path.Combine(CaminhoDaPasta, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new ErroDeOrcamento("Não foi possível abrir os dados. Feche outra janela do BankManager e tente novamente."); }
    }
    /// <summary>
    /// Resolve a pasta de dados: a indicada (testes) ou a pasta local da conta, compartilhada com as preferências.
    /// </summary>
    public static string PastaPadrao(string? diretorio = null) =>
        string.IsNullOrWhiteSpace(diretorio) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BankManager") : diretorio;
    /// <summary>
    /// Lê a base atual ou retorna um orçamento vazio apenas se o arquivo ainda não existe; erros não apagam dados.
    /// </summary>
    public DadosDoOrcamento Carregar() => File.Exists(CaminhoDoArquivo) ? Ler(CaminhoDoArquivo) : new();
    /// <summary>
    /// Lê e valida um JSON com limite de 20 MB; usado igualmente para base, importação e recuperação.
    /// </summary>
    public static DadosDoOrcamento Ler(string caminho)
    {
        using var fluxo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (fluxo.Length > 20 * 1024 * 1024) throw new ErroDeOrcamento("O arquivo deve ter no máximo 20 MB.");
        DadosDoOrcamento dados;
        try { dados = JsonSerializer.Deserialize<DadosDoOrcamento>(fluxo, OpcoesJson) ?? throw new ErroDeOrcamento("Arquivo vazio."); }
        catch (JsonException)
        {
            // Um arquivo de compartilhamento é outro tipo de arquivo: ele só ADICIONA despesas e tem o seu próprio caminho de importação.
            fluxo.Dispose();
            if (CompartilhamentoDeDespesas.PareceCompartilhamento(caminho)) throw new ErroDeOrcamento("Este arquivo é um compartilhamento de despesas, não um backup. Use “Importar despesas compartilhadas”. Os dados atuais foram preservados.");
            throw new ErroDeOrcamento("O arquivo não é um backup válido do BankManager. Os dados atuais foram preservados.");
        }
        // Arquivos de versões anteriores são atualizados em memória; o arquivo em disco só muda no próximo salvamento.
        MigracoesDeDados.ParaVersaoAtual(dados);
        // A leitura só entrega registros depois de validar as regras de negócio e todas as referências.
        RegrasDoOrcamento.Validar(dados);
        return dados;
    }
    /// <summary>
    /// Valida e grava o orçamento atual, preservando a versão existente como backup antes da substituição.
    /// </summary>
    public void Salvar(DadosDoOrcamento dados)
    {
        RegrasDoOrcamento.Validar(dados);
        GravarDeFormaAtomica(CaminhoDoArquivo, JsonSerializer.Serialize(dados, OpcoesJson), File.Exists(CaminhoDoArquivo) ? CaminhoDaCopia : null);
    }
    /// <summary>
    /// Valida e exporta uma cópia independente, sem trocar a base ativa nem manter backup do arquivo de destino.
    /// </summary>
    public static void Exportar(string caminho, DadosDoOrcamento dados)
    {
        RegrasDoOrcamento.Validar(dados);
        GravarDeFormaAtomica(caminho, JsonSerializer.Serialize(dados, OpcoesJson));
    }
    /// <summary>
    /// Grava em temporário no mesmo diretório, força flush e substitui o destino após o arquivo estar completo.
    /// </summary>
    /// <remarks>Uma falha é propagada ao chamador. O backup é atualizado antes da troca, e o temporário é removido ao sair quando possível.</remarks>
    internal static void GravarDeFormaAtomica(string caminho, string conteudo, string? backup = null)
    {
        // Aplica na saída o mesmo limite da entrada para não salvar uma base que o próprio aplicativo recusaria abrir.
        if (Encoding.UTF8.GetByteCount(conteudo) > 20 * 1024 * 1024) throw new ErroDeOrcamento("O conjunto de dados excede o limite de 20 MB. Nenhuma alteração foi salva.");
        // Mesmo diretório mantém a renomeação no mesmo sistema de arquivos; nome único evita colisões.
        var temporario = caminho + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var fluxo = new FileStream(temporario, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporario, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                // O writer libera seu buffer antes do Flush(true), que solicita também a descarga do buffer do sistema.
                using var escritor = new StreamWriter(fluxo, leaveOpen: true);
                escritor.Write(conteudo); escritor.Flush(); fluxo.Flush(true);
            }
            // Só substitui o principal depois que a cópia anterior foi preservada com sucesso.
            if (backup is not null) File.Copy(caminho, backup, true);
            File.Move(temporario, caminho, true);
        }
        finally { if (File.Exists(temporario)) File.Delete(temporario); }
    }
    /// <summary>
    /// Cria uma cópia profunda pelo formato JSON, isolando listas e registros durante uma tentativa de edição.
    /// </summary>
    public static DadosDoOrcamento Clonar(DadosDoOrcamento dados) => JsonSerializer.Deserialize<DadosDoOrcamento>(JsonSerializer.Serialize(dados, OpcoesJson), OpcoesJson)!;
    /// <summary>
    /// Fecha o handle do arquivo de bloqueio para permitir a abertura por outra instância.
    /// </summary>
    public void Dispose() => bloqueio?.Dispose();
}
