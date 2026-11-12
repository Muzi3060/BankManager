using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using static BankManager.Componentes;

namespace BankManager;
// Diálogos sobrepostos: um cartão branco dentro da própria janela, sobre uma camada escura. Usados pelo fluxo de compartilhar despesas.
public sealed partial class JanelaPrincipal
{
    // Camada que cobre a janela inteira: a camada escura e o cartão do diálogo. Fica oculta quando não há diálogo aberto.
    private readonly Panel camadaDaSobreposicao = new() { IsVisible = false };
    // O que a tecla Esc faz no diálogo aberto (cada diálogo decide: fechar, ou pedir confirmação antes).
    private Action? escDaSobreposicao;
    // Controle que tinha o foco antes de o diálogo abrir; recebe o foco de volta ao fechar.
    private Control? focoDeRetorno;
    private bool SobreposicaoAberta => camadaDaSobreposicao.IsVisible;
    /// <summary>
    /// Mostra um cartão como diálogo modal dentro da janela. A camada escura bloqueia cliques no que está atrás, o Tab circula só
    /// dentro do cartão (foco preso) e o foco inicial vai para <paramref name="foco"/> (o título). Trocar de passo reutiliza a camada.
    /// </summary>
    private void MostrarSobreposicao(Border cartao, Action aoTeclarEsc, Control foco)
    {
        if (!SobreposicaoAberta) focoDeRetorno = FocusManager?.GetFocusedElement() as Control;
        camadaDaSobreposicao.Children.Clear(); camadaDaSobreposicao.Children.Add(new Border().ComClasse("camada-escura")); camadaDaSobreposicao.Children.Add(cartao);
        escDaSobreposicao = aoTeclarEsc; camadaDaSobreposicao.IsVisible = true; AjustarSobreposicao();
        // O foco só pode ser dado depois que o cartão entra na árvore visual.
        Dispatcher.UIThread.Post(() => foco.Focus(), DispatcherPriority.Loaded);
    }
    /// <summary>
    /// Fecha o diálogo e devolve o foco ao controle que o abriu, se ele ainda estiver na tela.
    /// </summary>
    private void FecharSobreposicao()
    {
        camadaDaSobreposicao.Children.Clear(); camadaDaSobreposicao.IsVisible = false; escDaSobreposicao = null;
        var retorno = focoDeRetorno; focoDeRetorno = null;
        if (retorno is not null && retorno.IsEffectivelyVisible && retorno.IsEffectivelyEnabled) retorno.Focus();
    }
    // Limita a altura do cartão à janela; o miolo do cartão rola quando o conteúdo é maior.
    private void AjustarSobreposicao()
    {
        foreach (var cartao in camadaDaSobreposicao.Children.OfType<Border>().Where(b => b.Classes.Contains("modal"))) cartao.MaxHeight = Math.Max(320, camadaDaSobreposicao.Bounds.Height - 48);
    }
    /// <summary>
    /// Monta o cartão de um diálogo: título e subtítulo, botão "X" de fechar, indicador de passos opcional, miolo rolável e rodapé.
    /// O título é focalizável: recebe o foco ao abrir, para leitores de tela anunciarem onde a pessoa está.
    /// </summary>
    /// <returns>O cartão e o título (para receber o foco).</returns>
    private static (Border Cartao, TextBlock Titulo) CartaoModal(string titulo, string subtitulo, Control corpo, Control rodape, Action fechar, Control? passos = null, double largura = 700)
    {
        var tituloDaSecao = Texto(titulo, "modal-titulo"); tituloDaSecao.Focusable = true; AutomationProperties.SetHeadingLevel(tituloDaSecao, 1);
        var titulos = Pilha(tituloDaSecao, Texto(subtitulo, "modal-subtitulo")); titulos.Spacing = 4;
        var x = Botao("Fechar", fechar, "fechar", "Fechar", soIcone: true); Grid.SetColumn(x, 1);
        var cabecalho = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { titulos, x } };
        var rolagem = new ScrollViewer { Content = corpo, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Margin = new Thickness(0, 18, 0, 0) }; Grid.SetRow(rolagem, 2);
        rodape.Margin = new Thickness(0, 22, 0, 0); Grid.SetRow(rodape, 3);
        var leiaute = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Children = { cabecalho, rolagem, rodape } };
        if (passos is not null) { passos.Margin = new Thickness(0, 18, 0, 0); Grid.SetRow(passos, 1); leiaute.Children.Add(passos); }
        var cartao = new Border { Child = leiaute, Width = largura }.ComClasse("modal");
        // Papel de diálogo para tecnologias assistivas, com o título como nome; o Tab não sai do cartão.
        AutomationProperties.SetName(cartao, titulo); AutomationProperties.SetControlTypeOverride(cartao, Avalonia.Automation.Peers.AutomationControlType.Window);
        KeyboardNavigation.SetTabNavigation(cartao, KeyboardNavigationMode.Cycle);
        return (cartao, tituloDaSecao);
    }
    /// <summary>
    /// Rodapé padrão: um botão opcional à esquerda ("Voltar") e os demais à direita.
    /// </summary>
    private static Grid RodapeModal(Control? esquerda, params Control[] direita)
    {
        var botoes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom }; foreach (var b in direita) botoes.Children.Add(b);
        Grid.SetColumn(botoes, 1); var rodape = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { botoes } };
        if (esquerda is not null) { esquerda.HorizontalAlignment = HorizontalAlignment.Left; esquerda.VerticalAlignment = VerticalAlignment.Bottom; rodape.Children.Add(esquerda); }
        return rodape;
    }
    /// <summary>
    /// Aviso com ícone. Tipos: nulo (neutro), "warn" (âmbar), "positive" (sucesso) e "negative" (erro).
    /// </summary>
    private static Border Faixa(string icone, Control conteudo, string? tipo = null)
    {
        var simbolo = Icone(icone, 18); simbolo.VerticalAlignment = VerticalAlignment.Top; simbolo.Margin = new Thickness(0, 1, 12, 0); Grid.SetColumn(conteudo, 1);
        var faixa = new Border { Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { simbolo, conteudo } } }.ComClasse("faixa");
        if (tipo is not null) faixa.Classes.Add(tipo);
        return faixa;
    }
    /// <summary>
    /// Aviso de texto simples.
    /// </summary>
    private static Border Faixa(string icone, string texto, string? tipo = null) => Faixa(icone, Texto(texto), tipo);
    /// <summary>
    /// Linha "rótulo à esquerda, valor em destaque à direita", com divisória embaixo; usada nos resumos.
    /// </summary>
    private static Control LinhaDeResumo(string rotulo, string valorLido, bool divisoria = true)
    {
        var nome = Texto(rotulo, "apagado"); nome.VerticalAlignment = VerticalAlignment.Center;
        var valor = Texto(valorLido, "forte"); valor.TextAlignment = Avalonia.Media.TextAlignment.Right; valor.Margin = new Thickness(16, 0, 0, 0); Grid.SetColumn(valor, 1);
        AutomationProperties.SetName(valor, $"{rotulo}: {valorLido}");
        var fileira = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 10), Children = { nome, valor } };
        if (!divisoria) return fileira;
        var painel = new StackPanel(); painel.Children.Add(fileira); painel.Children.Add(Divisoria()); return painel;
    }
}
