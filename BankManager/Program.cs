using Avalonia;

namespace BankManager;
/// <summary>
/// Ponto de entrada do executável desktop; configura o Avalonia e mantém seu ciclo de vida.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Inicia a aplicação em uma thread STA, necessária à integração com recursos nativos do desktop no Windows.
    /// </summary>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    /// <summary>
    /// Seleciona o backend do sistema, registra a fonte Inter e habilita diagnóstico pelo mecanismo de Trace.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
