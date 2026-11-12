using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using BankManager.Core;

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
    /// Aplica a preferência de tema na hora: "Auto" volta a seguir o sistema, os demais forçam claro ou escuro.
    /// Todos os controles usam DynamicResource, então a troca vale para as janelas já abertas.
    /// </summary>
    public static void ApplyTheme(ThemePreference preference)
    {
        if (Current is null) return;
        Current.RequestedThemeVariant = preference switch { ThemePreference.Light => ThemeVariant.Light, ThemePreference.Dark => ThemeVariant.Dark, _ => ThemeVariant.Default };
    }
    /// <summary>
    /// Cria a janela principal quando o framework termina de inicializar no modo desktop clássico.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
