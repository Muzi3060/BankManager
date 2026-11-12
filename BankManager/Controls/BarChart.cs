using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;

namespace BankManager.Controls;
/// <summary>
/// Uma barra do gráfico: rótulo curto do eixo (por exemplo "nov"), valor e o texto mostrado no balão.
/// </summary>
/// <param name="Label">Rótulo sob a barra.</param>
/// <param name="Value">Valor representado; nunca negativo.</param>
/// <param name="Display">Texto do balão, já formatado (por exemplo "R$ 4.520,10").</param>
public sealed record BarPoint(string Label, decimal Value, string Display);
/// <summary>
/// Gráfico de barras simples, desenhado pelo próprio controle (sem biblioteca externa). O eixo sempre começa em zero,
/// para não exagerar diferenças; as linhas de grade são pontilhadas e os rótulos são calculados a partir dos dados.
/// A barra selecionada fica destacada com um balão; ao passar o mouse sobre outra barra, o balão mostra o valor dela.
/// As cores chegam pelo estilo em Controls.axaml (tokens do tema), então o gráfico acompanha a troca de tema.
/// </summary>
public sealed class BarChart : Control
{
    /// <summary>Cor das barras comuns.</summary>
    public static readonly StyledProperty<IBrush?> BarBrushProperty = AvaloniaProperty.Register<BarChart, IBrush?>(nameof(BarBrush));
    /// <summary>Cor da barra selecionada.</summary>
    public static readonly StyledProperty<IBrush?> SelectedBrushProperty = AvaloniaProperty.Register<BarChart, IBrush?>(nameof(SelectedBrush));
    /// <summary>Cor das linhas pontilhadas do eixo.</summary>
    public static readonly StyledProperty<IBrush?> GridBrushProperty = AvaloniaProperty.Register<BarChart, IBrush?>(nameof(GridBrush));
    /// <summary>Cor dos rótulos dos eixos.</summary>
    public static readonly StyledProperty<IBrush?> LabelBrushProperty = AvaloniaProperty.Register<BarChart, IBrush?>(nameof(LabelBrush));
    /// <summary>Fundo do balão com o valor.</summary>
    public static readonly StyledProperty<IBrush?> BalloonBrushProperty = AvaloniaProperty.Register<BarChart, IBrush?>(nameof(BalloonBrush));
    /// <summary>Texto do balão.</summary>
    public static readonly StyledProperty<IBrush?> BalloonTextBrushProperty = AvaloniaProperty.Register<BarChart, IBrush?>(nameof(BalloonTextBrush));
    public IBrush? BarBrush { get => GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public IBrush? SelectedBrush { get => GetValue(SelectedBrushProperty); set => SetValue(SelectedBrushProperty, value); }
    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush? LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public IBrush? BalloonBrush { get => GetValue(BalloonBrushProperty); set => SetValue(BalloonBrushProperty, value); }
    public IBrush? BalloonTextBrush { get => GetValue(BalloonTextBrushProperty); set => SetValue(BalloonTextBrushProperty, value); }
    // Espaços reservados: balão acima, rótulos dos meses abaixo e rótulos de valor à esquerda.
    private const double Top = 34, Bottom = 24, Left = 58, MinBar = 3;
    private readonly IReadOnlyList<BarPoint> points;
    private readonly int selected;
    // Barra sob o mouse (-1 quando nenhuma); tem prioridade sobre a selecionada para o balão.
    private int hovered = -1;
    static BarChart() => AffectsRender<BarChart>(BarBrushProperty, SelectedBrushProperty, GridBrushProperty, LabelBrushProperty, BalloonBrushProperty, BalloonTextBrushProperty);
    /// <summary>
    /// Cria o gráfico com os pontos na ordem em que serão desenhados e o índice da barra destacada.
    /// </summary>
    public BarChart(IReadOnlyList<BarPoint> points, int selected)
    {
        this.points = points; this.selected = selected; MinHeight = 120;
    }
    /// <summary>
    /// Escolhe o topo do eixo e o passo entre as linhas: um número "redondo" (1, 2, 2,5 ou 5 vezes uma potência de dez)
    /// que cubra o maior valor com três ou quatro linhas. O eixo começa sempre em zero.
    /// </summary>
    public static (decimal Top, decimal Step) Axis(decimal max)
    {
        if (max <= 0) return (0, 0);
        var rough = (double)max / 3; var power = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var factor = new[] { 1, 2, 2.5, 5, 10 }.First(f => f * power >= rough);
        var step = (decimal)(factor * power); return (Math.Ceiling(max / step) * step, step);
    }
    /// <summary>
    /// Rótulo compacto do eixo de valores: "0", "500", "1,5 mil", "2 mi".
    /// </summary>
    public static string AxisLabel(decimal value, CultureInfo culture) =>
        value >= 1_000_000 ? (value / 1_000_000).ToString("0.#", culture) + " mi" : value >= 1000 ? (value / 1000).ToString("0.#", culture) + " mil" : value.ToString("0.##", culture);
    private Rect Plot => new(Left, Top, Math.Max(1, Bounds.Width - Left), Math.Max(1, Bounds.Height - Top - Bottom));
    // Retângulo da barra i dentro da área de desenho; "full" devolve a faixa inteira (usada para detectar o mouse).
    private Rect BarRect(int i, decimal axisTop, bool full = false)
    {
        var plot = Plot; var slot = plot.Width / points.Count; var width = Math.Min(44, slot * 0.56);
        var height = axisTop <= 0 ? MinBar : Math.Max(MinBar, (double)(points[i].Value / axisTop) * plot.Height);
        var x = plot.X + slot * i + (slot - width) / 2;
        return full ? new Rect(plot.X + slot * i, 0, slot, Bounds.Height) : new Rect(x, plot.Bottom - height, width, height);
    }
    private FormattedText Label(string text, IBrush? brush, double size = 12, FontWeight weight = FontWeight.Normal) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, weight), size, brush);
    /// <summary>
    /// Desenha, nesta ordem: linhas pontilhadas com rótulos de valor, barras com rótulo do mês e o balão da barra em foco.
    /// </summary>
    public override void Render(DrawingContext context)
    {
        if (points.Count == 0) return;
        var plot = Plot; var (axisTop, step) = Axis(points.Max(p => p.Value)); var culture = CultureInfo.GetCultureInfo("pt-BR");
        var dotted = new Pen(GridBrush, 1, new DashStyle([2, 4], 0));
        // Linhas de grade a cada passo, de zero até o topo do eixo. Sem dados, só a linha do zero.
        for (var value = 0m; value <= axisTop; value += step == 0 ? 1 : step)
        {
            var y = Math.Round(plot.Bottom - (axisTop == 0 ? 0 : (double)(value / axisTop) * plot.Height)) + 0.5;
            context.DrawLine(dotted, new Point(plot.X, y), new Point(plot.Right, y));
            var text = Label(AxisLabel(value, culture), LabelBrush, 11); context.DrawText(text, new Point(plot.X - text.Width - 10, y - text.Height / 2));
        }
        var focus = hovered >= 0 ? hovered : selected;
        for (var i = 0; i < points.Count; i++)
        {
            var bar = BarRect(i, axisTop); var radius = Math.Min(8, bar.Height / 2);
            context.DrawRectangle(i == selected ? SelectedBrush : BarBrush, null, new RoundedRect(bar, radius, radius, 0, 0));
            var text = Label(points[i].Label, LabelBrush, 12, i == selected ? FontWeight.SemiBold : FontWeight.Normal);
            context.DrawText(text, new Point(bar.Center.X - text.Width / 2, plot.Bottom + 7));
        }
        if (focus < 0 || focus >= points.Count) return;
        // Balão: retângulo arredondado acima da barra em foco, mantido dentro da área do gráfico.
        var anchor = BarRect(focus, axisTop); var caption = Label(points[focus].Display, BalloonTextBrush, 12, FontWeight.SemiBold);
        var box = new Rect(0, 0, caption.Width + 20, caption.Height + 10);
        var left = Math.Clamp(anchor.Center.X - box.Width / 2, 0, Math.Max(0, Bounds.Width - box.Width)); var top = Math.Max(0, anchor.Y - box.Height - 6);
        context.DrawRectangle(BalloonBrush, null, new RoundedRect(new Rect(left, top, box.Width, box.Height), 8));
        context.DrawText(caption, new Point(left + 10, top + 5));
    }
    /// <summary>
    /// Passar o mouse sobre a faixa de uma barra mostra o valor dela no balão.
    /// </summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); var position = e.GetPosition(this); var (axisTop, _) = Axis(points.Count == 0 ? 0 : points.Max(p => p.Value));
        var index = -1; for (var i = 0; i < points.Count; i++) if (BarRect(i, axisTop, true).Contains(position)) index = i;
        if (index != hovered) { hovered = index; InvalidateVisual(); }
    }
    /// <summary>
    /// Ao sair do gráfico, o balão volta para a barra selecionada.
    /// </summary>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e); if (hovered != -1) { hovered = -1; InvalidateVisual(); }
    }
}
