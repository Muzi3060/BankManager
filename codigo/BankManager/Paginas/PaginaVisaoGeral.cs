using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using BankManager.Controles;
using BankManager.Nucleo;
using static BankManager.Componentes;

namespace BankManager;
// Página "Visão geral": saldo planejado com gráfico, resumo da casa, metas do mês, próximos pagamentos, acerto de contas e últimas movimentações.
public sealed partial class JanelaPrincipal
{
    /// <summary>
    /// Monta a visão geral, de cima para baixo. Sem pessoas cadastradas, mostra um convite com os primeiros passos.
    /// </summary>
    private Control VisaoGeral()
    {
        var despesas = MotorFinanceiro.Despesas(dados, mes); var renda = dados.Pessoas.Sum(p => MotorFinanceiro.Renda(dados, p.Id, mes)); var total = despesas.Sum(e => e.Valor);
        var emAberto = despesas.Where(e => !e.Paga).Sum(e => e.Valor);
        var resultado = Pilha(CabecalhoDaPagina("Seu mês, com clareza", null)); resultado.Spacing = 18;
        if (dados.Pessoas.Count == 0)
        {
            resultado.Children.Add(Cartao(Pilha(Texto("Vamos organizar suas finanças?", "secao"), Texto("1. Cadastre quem faz parte da casa e a renda deste mês.\n2. Adicione despesas individuais ou compartilhadas.\n3. Veja a divisão e acompanhe o pagamento.", "apagado"), Botao("Cadastrar primeira pessoa", () => _ = Executar(() => EditarPessoa()), "primario", "Mais"))));
            return resultado;
        }
        // 1. Saldo planejado com o gráfico (1,7) e o cartão escuro de resumo (1).
        var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("1.7*,16,*"), MinHeight = 330 };
        var resumo = Cartao(PainelDeResumo(renda, total, emAberto), "resumo"); Grid.SetColumn(resumo, 2); topo.Children.Add(Cartao(PainelDoSaldo(renda, total))); topo.Children.Add(resumo);
        resultado.Children.Add(topo);
        // Registro ausente é diferente de renda explicitamente zero; ambos entram como zero no cálculo.
        var faltando = dados.Pessoas.Where(p => !dados.Rendas.Any(i => i.IdDaPessoa == p.Id && i.Mes == mes)).ToList();
        if (faltando.Count > 0) resultado.Children.Add(AvisoNaPagina("Renda ainda não informada neste mês: " + string.Join(", ", faltando.Select(p => p.Nome)) + ". Informe em Pessoas e rendas; até lá, ela será considerada zero."));
        // 2. Metas do mês.
        resultado.Children.Add(SecaoDeMetas(despesas));
        // 3. Próximos pagamentos (1,5) e acerto de contas (1). O acerto só faz sentido com mais de uma pessoa.
        var proximos = CartaoDeProximos(despesas);
        if (dados.Pessoas.Count >= 2)
        {
            var meio = new Grid { ColumnDefinitions = new ColumnDefinitions("1.5*,16,*") }; var acerto = CartaoDeAcerto(); Grid.SetColumn(acerto, 2);
            meio.Children.Add(proximos); meio.Children.Add(acerto); resultado.Children.Add(meio);
        }
        else resultado.Children.Add(proximos);
        // 4. Últimas movimentações.
        resultado.Children.Add(CartaoDeMovimentacoes());
        return resultado;
    }
    /// <summary>
    /// Valor grande em reais: o "R$" (e o sinal de menos, se houver) em tamanho menor, e o número em destaque.
    /// </summary>
    private static TextBlock ValorGrande(decimal valor, string estilo)
    {
        var bloco = new TextBlock(); bloco.Classes.Add(estilo);
        bloco.Inlines!.Add(new Run((valor < 0 ? "− " : "") + "R$ ") { FontSize = 30, FontWeight = FontWeight.Normal });
        bloco.Inlines.Add(new Run(Math.Abs(valor).ToString("N2", Dinheiro.Cultura)));
        AutomationProperties.SetName(bloco, Dinheiro.Formatar(valor)); return bloco;
    }
    /// <summary>
    /// "Saldo planejado": o número em destaque, a conta que leva a ele e o gráfico das despesas dos últimos seis meses.
    /// </summary>
    private Control PainelDoSaldo(decimal renda, decimal total)
    {
        var saldo = ValorGrande(renda - total, "destaque"); if (renda < total) saldo.Classes.Add("negativo");
        var totais = MotorFinanceiro.TotaisMensais(dados, mes, 6);
        // Rótulo curto do mês ("nov"); o .NET devolve "nov." em português, então o ponto final é removido.
        var pontos = totais.Select(t => new PontoDoGrafico(Capitalizar(t.Mes.ToString("MMM", Dinheiro.Cultura).TrimEnd('.')), t.Total, Dinheiro.Formatar(t.Total))).ToList();
        var grafico = new GraficoDeBarras(pontos, pontos.Count - 1) { Margin = new Thickness(0, 6, 0, 0) };
        // Texto alternativo: quem usa leitor de tela recebe os mesmos valores que o gráfico mostra.
        AutomationProperties.SetName(grafico, "Despesas dos últimos 6 meses: " + string.Join("; ", totais.Select(t => $"{t.Mes.ToString("MMMM 'de' yyyy", Dinheiro.Cultura)}, {Dinheiro.Formatar(t.Total)}")) + ".");
        ToolTip.SetTip(grafico, "Despesas dos últimos 6 meses. Passe o mouse em uma barra para ver o valor.");
        var cabeca = Pilha(Texto("Saldo planejado", "forte"), saldo, Texto($"Renda de {Dinheiro.Formatar(renda)} menos despesas de {Dinheiro.Formatar(total)}", "apagado").ComClasse("pequeno")); cabeca.Spacing = 2;
        var grade = new Grid { RowDefinitions = new RowDefinitions("Auto,*") }; Grid.SetRow(grafico, 1); grade.Children.Add(cabeca); grade.Children.Add(grafico); return grade;
    }
    /// <summary>
    /// Primeira letra maiúscula ("nov" vira "Nov"), para os rótulos de mês.
    /// </summary>
    private static string Capitalizar(string texto) => texto.Length == 0 ? texto : char.ToUpper(texto[0], Dinheiro.Cultura) + texto[1..];
    /// <summary>
    /// Cartão escuro "Resumo da casa": no topo o título e o mês; embaixo, as despesas do mês, quanto da renda elas comprometem,
    /// a renda e o valor em aberto.
    /// </summary>
    private Control PainelDeResumo(decimal renda, decimal total, decimal emAberto)
    {
        var comprometido = renda > 0 ? Math.Round(total / renda * 100, MidpointRounding.AwayFromZero) : 0;
        var barra = new ProgressBar { Value = (double)Math.Min(100, comprometido), Maximum = 100, Height = 6, Margin = new Thickness(0, 8, 0, 2) }; barra.Classes.Add("sobre-resumo");
        var rotulo = Texto(renda > 0 ? $"{comprometido:0}% da renda comprometida" : "Renda do mês ainda não informada", "apagado"); rotulo.Classes.Add("pequeno");
        var valorLido = Texto(Dinheiro.Formatar(total), "indicador"); valorLido.FontSize = 32; valorLido.FontWeight = FontWeight.SemiBold;
        // Rodapé com dois valores lado a lado.
        Control Par(string nome, decimal valor) { var par = Pilha(Texto(nome, "apagado").ComClasse("pequeno"), Texto(Dinheiro.Formatar(valor), "forte")); par.Spacing = 2; return par; }
        var rodape = Colunas(Par("Renda", renda), Par("Em aberto", emAberto)); rodape.Margin = new Thickness(0, 14, 0, 0);
        var inferior = Pilha(Texto("Despesas do mês", "apagado"), valorLido, barra, rotulo, rodape); inferior.Spacing = 2; inferior.VerticalAlignment = VerticalAlignment.Bottom; Grid.SetRow(inferior, 1);
        var nomeDoMes = Texto(Capitalizar(mes.ToString("MMMM", Dinheiro.Cultura)), "apagado"); Grid.SetColumn(nomeDoMes, 1);
        var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { Texto("Resumo da casa", "forte"), nomeDoMes } };
        return new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { topo, inferior } };
    }
    /// <summary>
    /// Cabeçalho de seção: título à esquerda e, opcionalmente, um link de ação à direita.
    /// </summary>
    private static Grid TituloDeSecao(string titulo, Button? acao = null)
    {
        var tituloDaSecao = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; var rotulo = Texto(titulo, "secao"); rotulo.VerticalAlignment = VerticalAlignment.Center; tituloDaSecao.Children.Add(rotulo);
        if (acao is not null) { Grid.SetColumn(acao, 1); tituloDaSecao.Children.Add(acao); }
        return tituloDaSecao;
    }
    /// <summary>
    /// Aviso discreto dentro da página, para situações que pedem atenção sem bloquear nada.
    /// </summary>
    private static Control AvisoNaPagina(string texto)
    {
        var caixa = new Border { Child = Texto(texto, "alerta") }; caixa.Classes.Add("aviso"); return caixa;
    }
    /// <summary>
    /// "Metas do mês": até três cartões lado a lado, começando pelas metas mais próximas (ou acima) do limite.
    /// Sem metas, um convite para criar a primeira.
    /// </summary>
    private Control SecaoDeMetas(IReadOnlyList<Despesa> despesas)
    {
        var metas = MotorDeMetas.Progresso(dados, mes).OrderByDescending(g => g.Fracao).ToList();
        var secao = Pilha(TituloDeSecao("Metas do mês", Botao("Ver todas", () => { abaDePrevisoes = 0; IrPara(3); }, "link"))); secao.Spacing = 8;
        if (metas.Count == 0)
        {
            var criar = Botao("Criar primeira meta", () => _ = Executar(GerenciarMetas), "escuro"); criar.HorizontalAlignment = HorizontalAlignment.Left;
            var vazio = Pilha(Texto("Nenhuma meta ainda", "forte"), Texto("Defina quanto quer gastar por mês em uma categoria, como Mercado ou Lazer. O BankManager avisa quando você chegar perto do limite.", "apagado"), criar); vazio.Spacing = 8;
            secao.Children.Add(Cartao(vazio)); return secao;
        }
        // Sempre três colunas: com uma ou duas metas, os cartões mantêm a mesma largura.
        var fileira = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*,16,*") };
        for (var i = 0; i < Math.Min(3, metas.Count); i++) { var cartao = CartaoDeMeta(metas[i], despesas); Grid.SetColumn(cartao, i * 2); fileira.Children.Add(cartao); }
        secao.Children.Add(fileira);
        return secao;
    }
    /// <summary>
    /// "Próximos pagamentos": as três contas em aberto mais próximas, com ícone da categoria, prazo, valor e menu de ações.
    /// </summary>
    private Control CartaoDeProximos(IReadOnlyList<Despesa> despesas)
    {
        var painel = Pilha(TituloDeSecao("Próximos pagamentos", Botao("Ver todas as despesas", () => IrPara(2), "link"))); painel.Spacing = 0;
        var proximos = despesas.Where(e => !e.Paga).Take(3).ToList();
        if (proximos.Count == 0)
        {
            var vazio = Pilha(Texto(despesas.Count == 0 ? "Nenhuma despesa neste mês ainda" : "Tudo em dia!", "forte"), Texto(despesas.Count == 0 ? "Use “Nova despesa” para registrar a primeira conta." : "Nenhum pagamento em aberto neste mês.", "apagado")); vazio.Spacing = 4; vazio.Margin = new Thickness(0, 12, 0, 0);
            painel.Children.Add(vazio);
        }
        foreach (var e in proximos)
        {
            painel.Children.Add(Divisoria());
            var titulo = Pilha(new TextBlock { Text = e.Titulo, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis }, Texto(SubtituloDaDespesa(e, true), "apagado").ComClasse("pequeno")); titulo.Spacing = 1; titulo.VerticalAlignment = VerticalAlignment.Center; titulo.Margin = new Thickness(12, 0);
            var etiqueta = EtiquetaDePrazo(e); var valor = Texto(Dinheiro.Formatar(e.Valor), "forte"); valor.TextAlignment = TextAlignment.Right; valor.VerticalAlignment = VerticalAlignment.Center; valor.MinWidth = 100; valor.Margin = new Thickness(12, 0, 4, 0);
            var mais = Botao("Mais ações", () => { }, "fantasma redondo", "Opcoes", soIcone: true); mais.Flyout = MenuDaDespesa(e);
            var fileira = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"), Margin = new Thickness(0, 10) };
            Control[] celulas = [CirculoComIcone(NomeDoIconeDaCategoria(e.Categoria)), titulo, etiqueta, valor, mais];
            for (var i = 0; i < celulas.Length; i++) { Grid.SetColumn(celulas[i], i); fileira.Children.Add(celulas[i]); }
            painel.Children.Add(fileira);
        }
        return Cartao(painel);
    }
    // Quantas movimentações a visão geral mostra.
    private const int LinhasDeMovimentacao = 6;
    /// <summary>
    /// "Últimas movimentações": rendas do mês e despesas já pagas, das mais recentes para as mais antigas. Todas as linhas têm
    /// opacidade total; o link "Ver tudo" indica que a lista continua na página de despesas.
    /// </summary>
    private Control CartaoDeMovimentacoes()
    {
        var painel = Pilha(TituloDeSecao("Últimas movimentações", Botao("Ver tudo", () => IrPara(2), "link"))); painel.Spacing = 0;
        var movimentacoes = MotorFinanceiro.MovimentacoesRecentes(dados, mes, LinhasDeMovimentacao);
        if (movimentacoes.Count == 0)
        {
            var vazio = Pilha(Texto("Nada por aqui ainda", "forte"), Texto("As rendas do mês (no dia 1) e as despesas pagas neste mês aparecem nesta lista, pela data do pagamento.", "apagado")); vazio.Spacing = 4; vazio.Margin = new Thickness(0, 12, 0, 0);
            painel.Children.Add(vazio);
        }
        for (var i = 0; i < movimentacoes.Count; i++)
        {
            var m = movimentacoes[i]; painel.Children.Add(Divisoria());
            var titulo = Pilha(new TextBlock { Text = m.Titulo, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis }, Texto($"{m.Categoria} · {m.Data:dd/MM}", "apagado").ComClasse("pequeno")); titulo.Spacing = 1; titulo.VerticalAlignment = VerticalAlignment.Center; titulo.Margin = new Thickness(12, 0);
            // Entradas em verde com "+", saídas em texto normal com "−" (o sinal de menos tipográfico).
            var valor = Texto((m.Entrada ? "+ " : "− ") + Dinheiro.Formatar(m.Valor), "forte"); if (m.Entrada) valor.Classes.Add("positivo"); valor.VerticalAlignment = VerticalAlignment.Center;
            var fileira = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 10) };
            Grid.SetColumn(titulo, 1); Grid.SetColumn(valor, 2); fileira.Children.Add(CirculoComIcone(m.Entrada ? "SetaParaBaixo" : NomeDoIconeDaCategoria(m.Categoria), m.Entrada)); fileira.Children.Add(titulo); fileira.Children.Add(valor);
            painel.Children.Add(fileira);
        }
        return Cartao(painel);
    }
}
