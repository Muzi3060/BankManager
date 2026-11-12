using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BankManager.Nucleo;
using static BankManager.Componentes;

namespace BankManager;
// Metas por categoria: o cartão de meta (usado na visão geral e em Previsões e metas) e o diálogo que define os valores.
public sealed partial class JanelaPrincipal
{
    /// <summary>
    /// Cartão branco de uma meta: valor gasto, "Categoria · meta R$ X", barra, percentual usado à esquerda e, à direita,
    /// o percentual restante ou quanto passou. Embaixo, chips com as despesas da categoria no mês.
    /// A barra é verde até 79%, âmbar de 80% a 100% e vermelha acima de 100%.
    /// </summary>
    /// <param name="larga">Versão em lista (página de metas): acrescenta, à direita do valor, quanto ainda resta em reais.</param>
    private Border CartaoDeMeta(ProgressoDaMeta meta, IReadOnlyList<Despesa> despesas, bool larga = false)
    {
        // Arredonda para baixo: "79%" nunca aparece em âmbar e "100%" só fica vermelho quando de fato passou.
        var usado = (int)Math.Floor(meta.Fracao * 100); var tom = meta.Nivel switch { NivelDaMeta.Acima => "negative", NivelDaMeta.Perto => "warn", _ => "positive" };
        var barra = new ProgressBar { Value = (double)Math.Min(100m, meta.Fracao * 100), Maximum = 100 }; if (meta.Nivel == NivelDaMeta.Acima) barra.Classes.Add("over"); else if (meta.Nivel == NivelDaMeta.Perto) barra.Classes.Add("warn");
        var rotuloDeUsado = Texto(meta.Nivel == NivelDaMeta.Acima ? $"{usado}% da meta" : $"{usado}% usado", tom); rotuloDeUsado.Classes.Add("small"); rotuloDeUsado.Classes.Add("strong");
        // Na lista, o valor que passou já aparece no topo do cartão; aqui fica o percentual.
        var resto = Texto(meta.Nivel != NivelDaMeta.Acima ? $"{100 - usado}% restante" : larga ? $"{usado - 100}% acima" : $"Acima em {Dinheiro.Formatar(meta.Gasto - meta.Meta)}", meta.Nivel == NivelDaMeta.Acima ? "negative" : "muted"); resto.Classes.Add("small"); resto.TextAlignment = TextAlignment.Right;
        var rotulos = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") }; Grid.SetColumn(resto, 1); rotulos.Children.Add(rotuloDeUsado); rotulos.Children.Add(resto);
        var etiquetas = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, -6) };
        var naCategoria = despesas.Where(e => e.Categoria == meta.Categoria).OrderByDescending(e => e.Valor).ToList();
        // Até três despesas, as maiores primeiro; o restante vira "+N".
        foreach (var e in naCategoria.Take(3)) { var etiqueta = Etiqueta(e.Titulo); ((TextBlock)etiqueta.Child!).TextTrimming = TextTrimming.CharacterEllipsis; etiqueta.MaxWidth = 150; etiqueta.Margin = new Thickness(0, 0, 6, 6); etiquetas.Children.Add(etiqueta); }
        if (naCategoria.Count > 3) { var extra = Etiqueta($"+{naCategoria.Count - 3}", "status"); extra.Margin = new Thickness(0, 0, 6, 6); etiquetas.Children.Add(extra); }
        if (naCategoria.Count == 0) { var nenhum = Etiqueta("Sem despesas no mês"); nenhum.Margin = new Thickness(0, 0, 6, 6); etiquetas.Children.Add(nenhum); }
        var cabeca = Pilha(Texto(Dinheiro.Formatar(meta.Gasto), "money"), Texto($"{meta.Categoria} · meta {Dinheiro.Formatar(meta.Meta)}", "support").ComClasse("small")); cabeca.Spacing = 2;
        // Menu "⋯" do cartão: editar as metas ou abrir as despesas da categoria.
        var mais = Botao("Ações da meta de " + meta.Categoria, () => { }, "square", "More", soIcone: true); mais.VerticalAlignment = VerticalAlignment.Top;
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        menu.Items.Add(AcaoDeMenu("Editar metas", "Target", () => _ = Executar(GerenciarMetas)));
        menu.Items.Add(AcaoDeMenu("Ver despesas de " + meta.Categoria, "List", () => { filtro = ""; filtroDeSituacao = 0; filtroDeCategoria = RegrasDoOrcamento.TodasAsCategorias(dados).IndexOf(meta.Categoria) + 1; IrPara(2); }));
        mais.Flyout = menu; Grid.SetColumn(mais, 1);
        Control topo = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { cabeca, mais } };
        if (larga)
        {
            // Na lista há espaço para o valor que resta (ou que passou), alinhado à direita.
            var esquerda = Texto(Dinheiro.Formatar(Math.Abs(meta.Meta - meta.Gasto)), "strong"); esquerda.FontSize = 16; esquerda.TextAlignment = TextAlignment.Right; if (meta.Nivel == NivelDaMeta.Acima) esquerda.Classes.Add("negative");
            var legenda = Texto(meta.Nivel == NivelDaMeta.Acima ? "Acima da meta" : "Restante", "muted").ComClasse("small"); legenda.TextAlignment = TextAlignment.Right;
            var lado = Pilha(legenda, esquerda); lado.Spacing = 2; Grid.SetColumn(lado, 2);
            var icone = CirculoComIcone(NomeDoIconeDaCategoria(meta.Categoria)); icone.Margin = new Thickness(0, 0, 14, 0); Grid.SetColumn(cabeca, 1);
            ((Grid)topo).Children.Clear(); lado.Margin = new Thickness(0, 0, 14, 0); Grid.SetColumn(mais, 3);
            topo = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Children = { icone, cabeca, lado, mais } };
        }
        var painel = Pilha(topo, barra, rotulos, etiquetas); painel.Spacing = 6;
        barra.Margin = new Thickness(0, 6, 0, 0);
        var cartao = Cartao(painel, "solid"); AutomationProperties.SetName(cartao, $"Meta de {meta.Categoria}: {Dinheiro.Formatar(meta.Gasto)} de {Dinheiro.Formatar(meta.Meta)}, {usado}% usado"); return cartao;
    }
    /// <summary>
    /// Diálogo de metas: um campo por categoria; vazio significa "sem meta". A meta vale para todos os meses.
    /// </summary>
    private async Task GerenciarMetas()
    {
        var entradas = RegrasDoOrcamento.TodasAsCategorias(dados).Select(c => (Nome: c, Caixa: CampoDeTexto(dados.Metas.FirstOrDefault(g => g.Categoria == c)?.Valor.ToString("0.00", Dinheiro.Cultura), "Sem meta", 12))).ToList();
        var fileiras = new StackPanel { Spacing = 8 };
        foreach (var (nome, caixa) in entradas)
        {
            AutomationProperties.SetName(caixa, "Meta de " + nome);
            var rotulo = new TextBlock { Text = nome, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
            var fileira = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,190") }; Grid.SetColumn(rotulo, 1); Grid.SetColumn(caixa, 2); fileira.Children.Add(CirculoComIcone(NomeDoIconeDaCategoria(nome))); fileira.Children.Add(rotulo); fileira.Children.Add(caixa); fileiras.Children.Add(fileira);
        }
        var erro = Texto("", "error");
        var painel = Pilha(Cabecalho("Metas por categoria", "Defina quanto quer gastar por mês em cada categoria. A meta vale para todos os meses; deixe em branco para não ter meta."), fileiras, erro);
        var dialogo = Dialogo("Metas por categoria", painel, 620); dialogo.Height = 700;
        painel.Children.Add(Linha(Botao("Cancelar", () => dialogo.Close()), Botao("Salvar metas", () =>
        {
            try
            {
                // Lê e valida tudo antes de gravar: uma meta com erro não deixa salvar as outras pela metade.
                var interpretadas = entradas.Select(i => (i.Nome, Valor: string.IsNullOrWhiteSpace(i.Caixa.Text) ? (decimal?)null : Dinheiro.Interpretar(i.Caixa.Text, "Meta de " + i.Nome, true))).ToList();
                Gravar(d => { foreach (var (nome, valor) in interpretadas) { if (valor is { } valorLido) MotorDeMetas.Definir(d, nome, valorLido); else MotorDeMetas.Limpar(d, nome); } }); dialogo.Close();
            }
            catch (Exception ex) { erro.Text = ErroAmigavel(ex); }
        }, "primary")));
        await dialogo.ShowDialog(this);
    }
}
