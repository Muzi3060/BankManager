using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace BankManager;
/// <summary>
/// Conecta os recursos definidos em App.axaml ao ciclo de vida desktop do Avalonia.
/// </summary>
public sealed partial class App : Application
{
    /// <summary>
    /// Carrega o tema e os estilos compartilhados antes da criação das janelas.
    /// </summary>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    /// <summary>
    /// Cria a janela principal quando o framework termina de inicializar no modo desktop clássico.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
