using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;

namespace BankManager;
/// <summary>
/// Fábricas de controles compartilhados. As classes visuais referenciam os estilos em App.axaml; não contêm regras financeiras.
/// </summary>
internal static class Ui
{
    private static WindowIcon? appIcon;
    /// <summary>
    /// Ícone do aplicativo (janela e diálogos), carregado uma vez a partir dos recursos embutidos.
    /// </summary>
    public static WindowIcon AppIcon => appIcon ??= new WindowIcon(AssetLoader.Open(new Uri("avares://BankManager/Assets/icon.png")));
    /// <summary>
    /// Cria um ícone vetorial pelo nome definido em Styles/Icons.axaml (por exemplo "Home" usa Icon.Home).
    /// Os traços são desenhados em uma área 24x24 e ampliados ou reduzidos para o tamanho pedido; a cor vem do estilo Path.icon.
    /// </summary>
    public static Control Icon(string name, double size = 18)
    {
        // A geometria não muda com o tema, então basta buscá-la uma vez; só a cor do traço é dinâmica (no estilo).
        if (Application.Current is null || !Application.Current.TryFindResource("Icon." + name, out var geometry) || geometry is not Geometry data) throw new ArgumentException("Ícone desconhecido: " + name);
        var path = new Avalonia.Controls.Shapes.Path { Data = data }; path.Classes.Add("icon");
        return new Viewbox { Width = size, Height = size, IsHitTestVisible = false, Child = new Canvas { Width = 24, Height = 24, Children = { path } } };
    }
    /// <summary>
    /// Cria texto com quebra automática e, opcionalmente, uma classe de estilo.
    /// </summary>
    public static TextBlock Text(string text, string? style = null)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (style is not null) block.Classes.Add(style);
        return block;
    }
    /// <summary>
    /// Organiza os controles verticalmente, usando o espaçamento padrão do aplicativo.
    /// </summary>
    public static StackPanel Stack(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 14 };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }
    /// <summary>
    /// Reutiliza a pilha padrão em orientação horizontal, preservando a ordem dos controles.
    /// </summary>
    public static StackPanel Row(params Control[] children)
    {
        var panel = Stack(children); panel.Orientation = Orientation.Horizontal; panel.Spacing = 10; return panel;
    }
    /// <summary>
    /// Envolve um conteúdo com a borda, o fundo e o espaçamento do estilo card.
    /// </summary>
    public static Border Card(Control content)
    {
        var border = new Border { Child = content }; border.Classes.Add("card"); return border;
    }
    /// <summary>
    /// Cria um botão e liga seu clique à ação recebida. Ações assíncronas da janela usam o tratamento de erros de Run.
    /// </summary>
    public static Button Button(string text, Action action, string? style = null, string? icon = null)
    {
        var button = new Button { Content = text };
        // Com ícone, o conteúdo vira ícone + rótulo; o nome de automação mantém o rótulo para leitores de tela e testes.
        if (icon is not null) { var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; row.Children.Add(Icon(icon)); row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }); button.Content = row; }
        AutomationProperties.SetName(button, text);
        if (style is not null) button.Classes.Add(style);
        button.Click += (_, _) => action(); return button;
    }
    /// <summary>
    /// Cria um campo com texto inicial, indicação de preenchimento e limite visual de caracteres; o domínio valida o conteúdo.
    /// </summary>
    public static TextBox Input(string? text = null, string? placeholder = null, int max = 120) => new() { Text = text ?? "", PlaceholderText = placeholder, MaxLength = max };
    /// <summary>
    /// Associa rótulo e ajuda ao controle, incluindo metadados de acessibilidade para tecnologias assistivas.
    /// </summary>
    public static Control Field(string title, Control input, string? hint = null)
    {
        AutomationProperties.SetName(input, title);
        if (hint is not null) AutomationProperties.SetHelpText(input, hint);
        return hint is null ? Stack(Text(title), input) : Stack(Text(title), input, Text(hint, "muted"));
    }
    /// <summary>
    /// Distribui controles em colunas de mesma largura, aplicando espaço apenas entre as colunas.
    /// </summary>
    public static Grid Columns(params Control[] children)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", children.Select(_ => "*"))) };
        for (int i = 0; i < children.Length; i++)
        {
            children[i].Margin = new Thickness(i == 0 ? 0 : 8, 0, i == children.Length - 1 ? 0 : 8, 0);
            Grid.SetColumn(children[i], i); grid.Children.Add(children[i]);
        }
        return grid;
    }
    /// <summary>
    /// Cria uma seleção por índice; o chamador mantém a correspondência entre índices e opções do domínio.
    /// </summary>
    public static ComboBox Combo(IEnumerable<string> options, int index = 0) => new() { ItemsSource = options.ToArray(), SelectedIndex = index };
    /// <summary>
    /// Cria uma janela centralizada no proprietário e com rolagem para formulários maiores que a área disponível.
    /// </summary>
    public static Window Dialog(string title, Control content, double width = 620) => new()
    {
        Title = title, Width = width, Height = 720, MinWidth = 480, MinHeight = 400, Icon = AppIcon,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Content = new ScrollViewer { Content = new Border { Padding = new Thickness(28), Child = content } }
    };
    /// <summary>
    /// Abre uma confirmação modal. Somente o botão Confirmar devolve verdadeiro; cancelar ou fechar não autoriza a ação.
    /// </summary>
    public static async Task<bool> Confirm(Window owner, string title, string message)
    {
        var panel = Stack(Text(title, "section"), Text(message, "muted"));
        var dialog = Dialog(title, panel); dialog.Height = 320;
        panel.Children.Add(Row(Button("Cancelar", () => dialog.Close(false)), Button("Confirmar", () => dialog.Close(true), "primary")));
        return await dialog.ShowDialog<bool>(owner);
    }
}
