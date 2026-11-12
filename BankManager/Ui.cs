using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using BankManager.Core;

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
    /// Envolve um conteúdo em um cartão. Estilos: "card" (translúcido, o padrão), "solid" (branco, usado nas metas)
    /// e "summary" (escuro, do resumo da casa).
    /// </summary>
    public static Border Card(Control content, string style = "card")
    {
        var border = new Border { Child = content }; border.Classes.Add(style); return border;
    }
    /// <summary>
    /// Cria um botão e liga seu clique à ação recebida. Ações assíncronas da janela usam o tratamento de erros de Run.
    /// </summary>
    /// <param name="iconOnly">Mostra só o ícone (por exemplo as setas do mês); o rótulo continua como dica e nome de automação.</param>
    public static Button Button(string text, Action action, string? style = null, string? icon = null, bool iconOnly = false)
    {
        var button = new Button { Content = text };
        // Botão só com ícone: quadrado de 40 px; o texto vira dica (tooltip) e nome para leitores de tela.
        if (icon is not null && iconOnly) { button.Content = Icon(icon); button.MinWidth = 40; button.Padding = new Thickness(0); ToolTip.SetTip(button, text); }
        // Com ícone, o conteúdo vira ícone + rótulo; o nome de automação mantém o rótulo para leitores de tela e testes.
        else if (icon is not null) { var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; row.Children.Add(Icon(icon)); row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }); button.Content = row; }
        AutomationProperties.SetName(button, text);
        // Aceita várias classes separadas por espaço (por exemplo "ghost danger").
        if (style is not null) foreach (var name in style.Split(' ', StringSplitOptions.RemoveEmptyEntries)) button.Classes.Add(name);
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
        // A classe "dialog" troca o degradê da janela pela superfície sólida.
        Classes = { "dialog" },
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
    /// <summary>
    /// Pede um texto curto em um diálogo. Devolve o texto digitado ou nulo se o usuário cancelar; a validação fica com quem chama.
    /// </summary>
    public static async Task<string?> Prompt(Window owner, string title, string label, string initial, int max)
    {
        var input = Input(initial, null, max); string? result = null;
        var panel = Stack(Text(title, "section"), Field(label, input));
        var dialog = Dialog(title, panel, 480); dialog.Height = 300;
        panel.Children.Add(Row(Button("Cancelar", () => dialog.Close()), Button("Salvar", () => { result = input.Text; dialog.Close(); }, "primary")));
        await dialog.ShowDialog(owner); return result;
    }
    /// <summary>
    /// Linha divisória fina entre itens de uma lista.
    /// </summary>
    public static Border Divider() { var line = new Border(); line.Classes.Add("divider"); return line; }
    /// <summary>
    /// Etiqueta curta de situação. Tons: "warn", "positive", "negative"; sem tom, o chip é neutro.
    /// </summary>
    public static Border Chip(string text, string? tone = null)
    {
        var chip = new Border { Child = new TextBlock { Text = text } }; chip.Classes.Add("chip"); if (tone is not null) chip.Classes.Add(tone); return chip;
    }
    /// <summary>
    /// Círculo com a inicial do nome, usado para identificar pessoas. O forte (preto) destaca uma pessoa ao lado de outra.
    /// </summary>
    public static Border Avatar(string name, bool strong = false)
    {
        var initial = name.Trim().Length > 0 ? name.Trim()[..1].ToUpper(Money.Culture) : "?";
        var avatar = new Border { Child = new TextBlock { Text = initial }, VerticalAlignment = VerticalAlignment.Center }; avatar.Classes.Add("avatar"); avatar.Classes.Set("strong", strong); return avatar;
    }
    /// <summary>
    /// Nome do ícone de uma categoria. As oito categorias padrão têm ícone próprio; as criadas pelo usuário usam a etiqueta.
    /// </summary>
    public static string CategoryIconName(string category) => category switch
    {
        "Moradia" => "Home", "Mercado" => "Cart", "Transporte" => "Car", "Saúde" => "Heart", "Educação" => "Book", "Lazer" => "Smile", "Serviços" => "Zap", _ => "Tag"
    };
    /// <summary>
    /// Círculo de 36 px com um ícone. Com "income", fica verde (entrada de dinheiro).
    /// </summary>
    public static Border IconCircle(string icon, bool income = false)
    {
        var circle = new Border { Child = Icon(icon, 17) }; circle.Classes.Add("iconcircle"); circle.Classes.Set("income", income); return circle;
    }
    /// <summary>
    /// Controle de opções lado a lado (apenas uma ativa). Atualiza o destaque sozinho e avisa a opção escolhida.
    /// </summary>
    public static Border Segmented(string[] options, int selected, Action<int> changed)
    {
        var buttons = new List<Button>(); var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        for (var i = 0; i < options.Length; i++)
        {
            var index = i; var button = Button(options[i], () => { for (var j = 0; j < buttons.Count; j++) buttons[j].Classes.Set("active", j == index); changed(index); }, "seg");
            button.Classes.Set("active", i == selected); buttons.Add(button); row.Children.Add(button);
        }
        var box = new Border { Child = row, VerticalAlignment = VerticalAlignment.Center }; box.Classes.Add("segmented"); return box;
    }
    /// <summary>
    /// Seção de ajuda que abre e fecha ao clicar no título, no lugar de um texto longo sempre visível.
    /// </summary>
    public static Control Collapsible(string title, string body)
    {
        var content = Text(body, "muted"); content.IsVisible = false; content.Margin = new Thickness(12, 0, 12, 12);
        var chevron = Icon("ChevronDown", 16); chevron.RenderTransform = new RotateTransform(-90);
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var label = new TextBlock { Text = title, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center }; chevron.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(chevron, 1);
        heading.Children.Add(label); heading.Children.Add(chevron);
        var header = new Button { Content = heading, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch }; header.Classes.Add("ghost");
        AutomationProperties.SetName(header, title);
        header.Click += (_, _) => { content.IsVisible = !content.IsVisible; chevron.RenderTransform = new RotateTransform(content.IsVisible ? 0 : -90); };
        var panel = new StackPanel { Spacing = 0 }; panel.Children.Add(header); panel.Children.Add(content); return panel;
    }
    /// <summary>
    /// Acrescenta uma classe de estilo e devolve o próprio controle, para compor miniaturas em uma só expressão.
    /// </summary>
    public static T WithClass<T>(this T control, string style) where T : StyledElement { control.Classes.Add(style); return control; }
}
