using System.Globalization;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;

namespace BankManager.Controles;
/// <summary>
/// Uma barra do gráfico: rótulo curto do eixo (por exemplo "nov"), valor e o texto mostrado no balão.
/// </summary>
/// <param name="Rotulo">Rótulo sob a barra.</param>
/// <param name="Valor">Valor representado; nunca negativo.</param>
/// <param name="Exibicao">Texto do balão, já formatado (por exemplo "R$ 4.520,10").</param>
public sealed record PontoDoGrafico(string Rotulo, decimal Valor, string Exibicao);
/// <summary>
/// Gráfico de barras simples, desenhado pelo próprio controle (sem biblioteca externa). O eixo sempre começa em zero,
/// para não exagerar diferenças; as linhas de grade são pontilhadas e os rótulos são calculados a partir dos dados.
/// A barra selecionada fica destacada com um balão; ao passar o mouse sobre outra barra, o balão mostra o valor dela.
/// As cores chegam pelo estilo em Controles.axaml (tokens do tema), então o gráfico acompanha a troca de tema.
/// </summary>
public sealed class GraficoDeBarras : Control
{
    /// <summary>Cor das barras comuns.</summary>
    public static readonly StyledProperty<IBrush?> CorDaBarraProperty = AvaloniaProperty.Register<GraficoDeBarras, IBrush?>(nameof(CorDaBarra));
    /// <summary>Cor da barra selecionada.</summary>
    public static readonly StyledProperty<IBrush?> CorDaSelecionadaProperty = AvaloniaProperty.Register<GraficoDeBarras, IBrush?>(nameof(CorDaSelecionada));
    /// <summary>Cor das linhas pontilhadas do eixo.</summary>
    public static readonly StyledProperty<IBrush?> CorDaGradeProperty = AvaloniaProperty.Register<GraficoDeBarras, IBrush?>(nameof(CorDaGrade));
    /// <summary>Cor dos rótulos dos eixos.</summary>
    public static readonly StyledProperty<IBrush?> CorDoRotuloProperty = AvaloniaProperty.Register<GraficoDeBarras, IBrush?>(nameof(CorDoRotulo));
    /// <summary>Fundo do balão com o valor.</summary>
    public static readonly StyledProperty<IBrush?> CorDoBalaoProperty = AvaloniaProperty.Register<GraficoDeBarras, IBrush?>(nameof(CorDoBalao));
    /// <summary>Texto do balão.</summary>
    public static readonly StyledProperty<IBrush?> CorDoTextoDoBalaoProperty = AvaloniaProperty.Register<GraficoDeBarras, IBrush?>(nameof(CorDoTextoDoBalao));
    public IBrush? CorDaBarra { get => GetValue(CorDaBarraProperty); set => SetValue(CorDaBarraProperty, value); }
    public IBrush? CorDaSelecionada { get => GetValue(CorDaSelecionadaProperty); set => SetValue(CorDaSelecionadaProperty, value); }
    public IBrush? CorDaGrade { get => GetValue(CorDaGradeProperty); set => SetValue(CorDaGradeProperty, value); }
    public IBrush? CorDoRotulo { get => GetValue(CorDoRotuloProperty); set => SetValue(CorDoRotuloProperty, value); }
    public IBrush? CorDoBalao { get => GetValue(CorDoBalaoProperty); set => SetValue(CorDoBalaoProperty, value); }
    public IBrush? CorDoTextoDoBalao { get => GetValue(CorDoTextoDoBalaoProperty); set => SetValue(CorDoTextoDoBalaoProperty, value); }
    // Espaços reservados: balão acima, rótulos dos meses abaixo e rótulos de valor à esquerda.
    private const double Superior = 34, Inferior = 24, Esquerda = 68, BarraMinima = 3;
    private readonly IReadOnlyList<PontoDoGrafico> pontos;
    private readonly int selecionada;
    // Barra sob o mouse (-1 quando nenhuma); tem prioridade sobre a selecionada para o balão.
    private int sobOMouse = -1;
    static GraficoDeBarras() => AffectsRender<GraficoDeBarras>(CorDaBarraProperty, CorDaSelecionadaProperty, CorDaGradeProperty, CorDoRotuloProperty, CorDoBalaoProperty, CorDoTextoDoBalaoProperty);
    /// <summary>
    /// Cria o gráfico com os pontos na ordem em que serão desenhados e o índice da barra destacada.
    /// </summary>
    public GraficoDeBarras(IReadOnlyList<PontoDoGrafico> pontos, int selecionada)
    {
        this.pontos = pontos; this.selecionada = selecionada; MinHeight = 120;
    }
    /// <summary>
    /// Escolhe o topo do eixo e o passo entre as linhas: um número "redondo" (1, 2, 2,5 ou 5 vezes uma potência de dez)
    /// que cubra o maior valor com três ou quatro linhas. O eixo começa sempre em zero.
    /// </summary>
    public static (decimal Superior, decimal Step) Eixo(decimal maximo)
    {
        if (maximo <= 0) return (0, 0);
        var aproximado = (double)maximo / 3; var potencia = Math.Pow(10, Math.Floor(Math.Log10(aproximado)));
        var fator = new[] { 1, 2, 2.5, 5, 10 }.First(f => f * potencia >= aproximado);
        var passo = (decimal)(fator * potencia); return (Math.Ceiling(maximo / passo) * passo, passo);
    }
    /// <summary>
    /// Rótulo compacto do eixo de valores: "R$ 0", "R$ 500", "R$ 1,5 mil", "R$ 2 mi".
    /// </summary>
    public static string RotuloDoEixo(decimal valorLido, CultureInfo cultura) =>
        "R$ " + (valorLido >= 1_000_000 ? (valorLido / 1_000_000).ToString("0.#", cultura) + " mi" : valorLido >= 1000 ? (valorLido / 1000).ToString("0.#", cultura) + " mil" : valorLido.ToString("0.##", cultura));
    private Rect AreaDoGrafico => new(Esquerda, Superior, Math.Max(1, Bounds.Width - Esquerda), Math.Max(1, Bounds.Height - Superior - Inferior));
    // Retângulo da barra i dentro da área de desenho; "full" devolve a faixa inteira (usada para detectar o mouse).
    private Rect RetanguloDaBarra(int i, decimal topoDoEixo, bool cheia = false)
    {
        var area = AreaDoGrafico; var vaga = area.Width / pontos.Count; var largura = Math.Min(34, vaga * 0.46);
        var altura = topoDoEixo <= 0 ? BarraMinima : Math.Max(BarraMinima, (double)(pontos[i].Valor / topoDoEixo) * area.Height);
        var x = area.X + vaga * i + (vaga - largura) / 2;
        return cheia ? new Rect(area.X + vaga * i, 0, vaga, Bounds.Height) : new Rect(x, area.Bottom - altura, largura, altura);
    }
    private FormattedText Rotulo(string texto, IBrush? pincel, double tamanho = 12, FontWeight peso = FontWeight.Normal) =>
        new(texto, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, peso), tamanho, pincel);
    /// <summary>
    /// Desenha, nesta ordem: linhas pontilhadas com rótulos de valor, barras com rótulo do mês e o balão da barra em foco.
    /// </summary>
    public override void Render(DrawingContext contexto)
    {
        if (pontos.Count == 0) return;
        var area = AreaDoGrafico; var (topoDoEixo, passo) = Eixo(pontos.Max(p => p.Valor)); var cultura = CultureInfo.GetCultureInfo("pt-BR");
        var pontilhado = new Pen(CorDaGrade, 1, new DashStyle([2, 4], 0));
        // Linhas de grade a cada passo, de zero até o topo do eixo. Sem dados, só a linha do zero.
        for (var valorLido = 0m; valorLido <= topoDoEixo; valorLido += passo == 0 ? 1 : passo)
        {
            var y = Math.Round(area.Bottom - (topoDoEixo == 0 ? 0 : (double)(valorLido / topoDoEixo) * area.Height)) + 0.5;
            contexto.DrawLine(pontilhado, new Point(area.X, y), new Point(area.Right, y));
            var texto = Rotulo(RotuloDoEixo(valorLido, cultura), CorDoRotulo, 11); contexto.DrawText(texto, new Point(area.X - texto.Width - 10, y - texto.Height / 2));
        }
        var foco = sobOMouse >= 0 ? sobOMouse : selecionada;
        for (var i = 0; i < pontos.Count; i++)
        {
            var barra = RetanguloDaBarra(i, topoDoEixo); var raio = Math.Min(8, barra.Height / 2);
            contexto.DrawRectangle(i == selecionada ? CorDaSelecionada : CorDaBarra, null, new RoundedRect(barra, raio));
            var texto = Rotulo(pontos[i].Rotulo, CorDoRotulo, 12, i == selecionada ? FontWeight.SemiBold : FontWeight.Normal);
            contexto.DrawText(texto, new Point(barra.Center.X - texto.Width / 2, area.Bottom + 7));
        }
        if (foco < 0 || foco >= pontos.Count) return;
        // Balão: retângulo arredondado acima da barra em foco, mantido dentro da área do gráfico.
        var ancora = RetanguloDaBarra(foco, topoDoEixo); var legenda = Rotulo(pontos[foco].Exibicao, CorDoTextoDoBalao, 12, FontWeight.SemiBold);
        var caixa = new Rect(0, 0, legenda.Width + 20, legenda.Height + 10);
        var esquerda = Math.Clamp(ancora.Center.X - caixa.Width / 2, 0, Math.Max(0, Bounds.Width - caixa.Width)); var topo = Math.Max(0, ancora.Y - caixa.Height - 6);
        contexto.DrawRectangle(CorDoBalao, null, new RoundedRect(new Rect(esquerda, topo, caixa.Width, caixa.Height), 8));
        contexto.DrawText(legenda, new Point(esquerda + 10, topo + 5));
    }
    /// <summary>
    /// Apresenta o gráfico às tecnologias assistivas como uma imagem; o texto alternativo (AutomationProperties.Nome) traz os valores.
    /// </summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new PeerDoGrafico(this);
    private sealed class PeerDoGrafico(Control dono) : ControlAutomationPeer(dono)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override bool IsContentElementCore() => true;
        protected override bool IsControlElementCore() => true;
    }
    /// <summary>
    /// Passar o mouse sobre a faixa de uma barra mostra o valor dela no balão.
    /// </summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); var posicao = e.GetPosition(this); var (topoDoEixo, _) = Eixo(pontos.Count == 0 ? 0 : pontos.Max(p => p.Valor));
        var indice = -1; for (var i = 0; i < pontos.Count; i++) if (RetanguloDaBarra(i, topoDoEixo, true).Contains(posicao)) indice = i;
        if (indice != sobOMouse) { sobOMouse = indice; InvalidateVisual(); }
    }
    /// <summary>
    /// Ao sair do gráfico, o balão volta para a barra selecionada.
    /// </summary>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e); if (sobOMouse != -1) { sobOMouse = -1; InvalidateVisual(); }
    }
}
