using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using static BankManager.Ui;

namespace BankManager;
// Diálogos sobrepostos: um cartão branco dentro da própria janela, sobre uma camada escura. Usados pelo fluxo de compartilhar despesas.
public sealed partial class MainWindow
{
    // Camada que cobre a janela inteira: a camada escura e o cartão do diálogo. Fica oculta quando não há diálogo aberto.
    private readonly Panel overlayHost = new() { IsVisible = false };
    // O que a tecla Esc faz no diálogo aberto (cada diálogo decide: fechar, ou pedir confirmação antes).
    private Action? overlayEscape;
    // Controle que tinha o foco antes de o diálogo abrir; recebe o foco de volta ao fechar.
    private Control? overlayReturnFocus;
    private bool OverlayOpen => overlayHost.IsVisible;
    /// <summary>
    /// Mostra um cartão como diálogo modal dentro da janela. A camada escura bloqueia cliques no que está atrás, o Tab circula só
    /// dentro do cartão (foco preso) e o foco inicial vai para <paramref name="focus"/> (o título). Trocar de passo reutiliza a camada.
    /// </summary>
    private void ShowOverlay(Border card, Action escape, Control focus)
    {
        if (!OverlayOpen) overlayReturnFocus = FocusManager?.GetFocusedElement() as Control;
        overlayHost.Children.Clear(); overlayHost.Children.Add(new Border().WithClass("scrim")); overlayHost.Children.Add(card);
        overlayEscape = escape; overlayHost.IsVisible = true; FitOverlay();
        // O foco só pode ser dado depois que o cartão entra na árvore visual.
        Dispatcher.UIThread.Post(() => focus.Focus(), DispatcherPriority.Loaded);
    }
    /// <summary>
    /// Fecha o diálogo e devolve o foco ao controle que o abriu, se ele ainda estiver na tela.
    /// </summary>
    private void CloseOverlay()
    {
        overlayHost.Children.Clear(); overlayHost.IsVisible = false; overlayEscape = null;
        var back = overlayReturnFocus; overlayReturnFocus = null;
        if (back is not null && back.IsEffectivelyVisible && back.IsEffectivelyEnabled) back.Focus();
    }
    // Limita a altura do cartão à janela; o miolo do cartão rola quando o conteúdo é maior.
    private void FitOverlay()
    {
        foreach (var card in overlayHost.Children.OfType<Border>().Where(b => b.Classes.Contains("modal"))) card.MaxHeight = Math.Max(320, overlayHost.Bounds.Height - 48);
    }
    /// <summary>
    /// Monta o cartão de um diálogo: título e subtítulo, botão "X" de fechar, indicador de passos opcional, miolo rolável e rodapé.
    /// O título é focalizável: recebe o foco ao abrir, para leitores de tela anunciarem onde a pessoa está.
    /// </summary>
    /// <returns>O cartão e o título (para receber o foco).</returns>
    private static (Border Card, TextBlock Title) ModalCard(string title, string subtitle, Control body, Control footer, Action close, Control? steps = null, double width = 700)
    {
        var heading = Text(title, "modal-title"); heading.Focusable = true; AutomationProperties.SetHeadingLevel(heading, 1);
        var titles = Stack(heading, Text(subtitle, "modal-sub")); titles.Spacing = 4;
        var x = Button("Fechar", close, "close", "Close", iconOnly: true); Grid.SetColumn(x, 1);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { titles, x } };
        var scroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Margin = new Thickness(0, 18, 0, 0) }; Grid.SetRow(scroll, 2);
        footer.Margin = new Thickness(0, 22, 0, 0); Grid.SetRow(footer, 3);
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Children = { header, scroll, footer } };
        if (steps is not null) { steps.Margin = new Thickness(0, 18, 0, 0); Grid.SetRow(steps, 1); layout.Children.Add(steps); }
        var card = new Border { Child = layout, Width = width }.WithClass("modal");
        // Papel de diálogo para tecnologias assistivas, com o título como nome; o Tab não sai do cartão.
        AutomationProperties.SetName(card, title); AutomationProperties.SetControlTypeOverride(card, Avalonia.Automation.Peers.AutomationControlType.Window);
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        return (card, heading);
    }
    /// <summary>
    /// Rodapé padrão: um botão opcional à esquerda ("Voltar") e os demais à direita.
    /// </summary>
    private static Grid ModalFooter(Control? left, params Control[] right)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom }; foreach (var b in right) buttons.Children.Add(b);
        Grid.SetColumn(buttons, 1); var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { buttons } };
        if (left is not null) { left.HorizontalAlignment = HorizontalAlignment.Left; left.VerticalAlignment = VerticalAlignment.Bottom; footer.Children.Add(left); }
        return footer;
    }
    /// <summary>
    /// Aviso com ícone. Tipos: nulo (neutro), "warn" (âmbar), "positive" (sucesso) e "negative" (erro).
    /// </summary>
    private static Border Banner(string icon, Control content, string? kind = null)
    {
        var glyph = Icon(icon, 18); glyph.VerticalAlignment = VerticalAlignment.Top; glyph.Margin = new Thickness(0, 1, 12, 0); Grid.SetColumn(content, 1);
        var banner = new Border { Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { glyph, content } } }.WithClass("banner");
        if (kind is not null) banner.Classes.Add(kind);
        return banner;
    }
    /// <summary>
    /// Aviso de texto simples.
    /// </summary>
    private static Border Banner(string icon, string text, string? kind = null) => Banner(icon, Text(text), kind);
    /// <summary>
    /// Linha "rótulo à esquerda, valor em destaque à direita", com divisória embaixo; usada nos resumos.
    /// </summary>
    private static Control SummaryLine(string label, string value, bool divider = true)
    {
        var name = Text(label, "muted"); name.VerticalAlignment = VerticalAlignment.Center;
        var amount = Text(value, "strong"); amount.TextAlignment = Avalonia.Media.TextAlignment.Right; amount.Margin = new Thickness(16, 0, 0, 0); Grid.SetColumn(amount, 1);
        AutomationProperties.SetName(amount, $"{label}: {value}");
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 10), Children = { name, amount } };
        if (!divider) return row;
        var panel = new StackPanel(); panel.Children.Add(row); panel.Children.Add(Divider()); return panel;
    }
}
