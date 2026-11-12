using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BankManager.Nucleo;
using static BankManager.Componentes;

namespace BankManager;
// Página "Previsões e metas": um submenu reúne as metas por categoria, a previsão do próximo mês, as despesas recorrentes e o acerto de contas.
public sealed partial class JanelaPrincipal
{
    /// <summary>
    /// Monta a página com o submenu interno. A aba escolhida fica guardada na janela, para sobreviver a um salvamento.
    /// Recorrentes e acerto continuam acessíveis também de onde já estavam (Despesas e Visão geral).
    /// </summary>
    private Control PaginaDePrevisoes()
    {
        var abas = Segmentado(["Metas por categoria", "Previsão do próximo mês", "Despesas recorrentes", "Acerto de contas"], abaDePrevisoes, i => { abaDePrevisoes = i; Desenhar(); });
        var painel = Pilha(CabecalhoDaPagina("Previsões e metas", "Metas, previsão, contas que se repetem e acerto de contas."), abas); painel.Spacing = 18;
        foreach (var bloco in abaDePrevisoes switch { 1 => AbaDePrevisao(), 2 => AbaDeRecorrencias(), 3 => AbaDeAcerto(), _ => AbaDeMetas() }) painel.Children.Add(bloco);
        return painel;
    }
    /// <summary>
    /// Estado vazio padrão de uma aba: título, explicação e, se houver, uma ação.
    /// </summary>
    private static Border CartaoVazio(string titulo, string corpo, Button? acao = null)
    {
        var vazio = Pilha(Texto(titulo, "secao"), Texto(corpo, "apagado")); vazio.Spacing = 8; vazio.Margin = new Thickness(4, 18);
        if (acao is not null) { acao.HorizontalAlignment = HorizontalAlignment.Left; vazio.Children.Add(acao); }
        return Cartao(vazio);
    }
    /// <summary>
    /// Aba de metas: um cartão branco por meta, em lista, com valor gasto, meta, restante, barra e as despesas da categoria.
    /// </summary>
    private IEnumerable<Control> AbaDeMetas()
    {
        var metas = MotorDeMetas.Progresso(dados, mes); var despesas = MotorFinanceiro.Despesas(dados, mes);
        if (metas.Count == 0) { yield return CartaoVazio("Nenhuma meta ainda", "Defina quanto quer gastar por mês em uma categoria, como Mercado ou Lazer. A barra fica verde até 79%, âmbar de 80% a 100% e vermelha quando passa do limite.", Botao("Criar primeira meta", () => _ = Executar(GerenciarMetas), "escuro")); yield break; }
        var acima = metas.Count(g => g.Nivel == NivelDaMeta.Acima);
        var resumo = Texto($"{metas.Count} {(metas.Count == 1 ? "meta" : "metas")} em {mes.ToString("MMMM 'de' yyyy", Dinheiro.Cultura)}" + (acima > 0 ? $" · {acima} acima do limite" : " · todas dentro do limite"), "apagado"); resumo.VerticalAlignment = VerticalAlignment.Center;
        var gerenciar = Botao("Gerenciar metas", () => _ = Executar(GerenciarMetas), "escuro", "Alvo"); Grid.SetColumn(gerenciar, 1);
        var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; topo.Children.Add(resumo); topo.Children.Add(gerenciar); yield return topo;
        foreach (var meta in metas) yield return CartaoDeMeta(meta, despesas, larga: true);
        yield return Texto("A meta é mensal e vale para todos os meses. Ela soma todas as despesas da categoria no mês, pagas ou em aberto.", "apagado");
    }
    /// <summary>
    /// Aba de previsão: estimativa para o mês seguinte ao selecionado, por categoria, com a explicação do cálculo em uma seção que abre e fecha.
    /// </summary>
    private IEnumerable<Control> AbaDePrevisao()
    {
        var destino = mes.AddMonths(1); var previsoes = MotorFinanceiro.Previsoes(dados, destino);
        yield return Texto($"Estimativa para {destino.ToString("MMMM 'de' yyyy", Dinheiro.Cultura)}, a partir do histórico até o mês selecionado.", "apagado");
        // Dois números lado a lado: quanto se espera gastar e quanto já está lançado.
        Control Indicador(string rotulo, decimal valorLido, string dica) { var indicador = Pilha(Texto(rotulo, "apagado"), Texto(Dinheiro.Formatar(valorLido), "indicador"), Texto(dica, "apagado").ComClasse("pequeno")); indicador.Spacing = 2; return indicador; }
        yield return Cartao(Colunas(Indicador("Estimativa total", previsoes.Sum(f => f.Estimativa), "Histórico + compromissos conhecidos"), Indicador("Já lançado no próximo mês", MotorFinanceiro.Despesas(dados, destino).Sum(e => e.Valor), "Inclui parcelas com vencimento no mês")));
        if (previsoes.Count == 0) yield return CartaoVazio("Ainda não há o que prever", "Adicione despesas para começar. Um mês com R$ 1.000,00 em mercado gera uma estimativa de R$ 1.000,00 para o próximo mês.");
        else
        {
            var fileiras = new StackPanel { Spacing = 0 };
            foreach (var previsao in previsoes)
            {
                if (fileiras.Children.Count > 0) fileiras.Children.Add(Divisoria());
                var informacao = Pilha(new TextBlock { Text = previsao.Categoria, FontWeight = FontWeight.SemiBold, FontSize = 13 }, Texto($"Média {Dinheiro.Formatar(previsao.Historico)} · {previsao.Meses} {(previsao.Meses == 1 ? "mês" : "meses")} de base · já lançado {Dinheiro.Formatar(previsao.Agendado)}", "apagado").ComClasse("pequeno")); informacao.Spacing = 1; informacao.VerticalAlignment = VerticalAlignment.Center; informacao.Margin = new Thickness(12, 0);
                var valor = Texto(Dinheiro.Formatar(previsao.Estimativa), "forte"); valor.VerticalAlignment = VerticalAlignment.Center; valor.TextAlignment = TextAlignment.Right; valor.Width = 120;
                var fileira = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(0, 10) }; Grid.SetColumn(informacao, 1); fileira.Children.Add(CirculoComIcone(NomeDoIconeDaCategoria(previsao.Categoria))); fileira.Children.Add(informacao);
                if (previsao.Meses < 3) { var etiqueta = Etiqueta("Pouco histórico", "alerta"); Grid.SetColumn(etiqueta, 2); fileira.Children.Add(etiqueta); }
                Grid.SetColumn(valor, 3); fileira.Children.Add(valor); fileiras.Children.Add(fileira);
            }
            yield return Cartao(fileiras);
        }
        var como = Cartao(Recolhivel("Como chegamos a esses valores", "Usamos a média de até 3 meses anteriores ao mês previsto. Desde o primeiro mês registrado, meses sem gastos contam como zero. Compras parceladas ficam fora da média: somamos apenas as parcelas que vencem no mês previsto.\n\nEm cada categoria, consideramos o maior valor entre a média e os gastos à vista já lançados; depois somamos as parcelas. Assim, evitamos contar duas vezes um gasto já previsto. São estimativas, não garantias de gastos futuros.")); como.Padding = new Thickness(6);
        yield return como;
    }
    /// <summary>
    /// Aba de recorrentes: a mesma lista do diálogo "Despesas recorrentes", direto na página.
    /// </summary>
    private IEnumerable<Control> AbaDeRecorrencias()
    {
        yield return Texto("Contas que se repetem todo mês. Cada mês é uma despesa comum: você pode pagar, editar ou excluir um mês sem afetar os outros.", "apagado");
        if (dados.Recorrencias.Count == 0) yield return CartaoVazio("Nenhuma despesa recorrente ainda", "Para aluguel, internet e assinaturas: ao criar uma despesa, marque “Repetir todo mês”.", Botao("Nova despesa", () => _ = Executar(() => EditarDespesa()), "escuro", "Mais"));
        // Na página, erros vão para o aviso global e a lista se refaz sozinha quando o salvamento reconstrói a tela.
        else yield return Cartao(LinhasDeRecorrencia(() => this, acao => Executar(acao)));
    }
    /// <summary>
    /// Aba de acerto: quem deve a quem e o histórico de acertos já registrados.
    /// </summary>
    private IEnumerable<Control> AbaDeAcerto()
    {
        if (dados.Pessoas.Count < 2) { yield return CartaoVazio("O acerto precisa de duas pessoas", "Quando mais de uma pessoa divide as despesas, o BankManager mostra aqui quem deve a quem. Cadastre outra pessoa em Pessoas e rendas."); yield break; }
        yield return CartaoDeAcerto(historico: false);
        var painelDoHistorico = Pilha(Texto("Histórico de acertos", "secao"), LinhasDeAcerto(() => this, () => { })); painelDoHistorico.Spacing = 8;
        yield return Cartao(painelDoHistorico);
    }
}
