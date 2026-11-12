using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BankManager.Nucleo;
using static BankManager.Componentes;

namespace BankManager;
// Diálogo "Despesas recorrentes": lista os modelos mensais e permite encerrar ou remover cada um.
public sealed partial class JanelaPrincipal
{
    /// <summary>
    /// Linhas da lista de recorrências, usadas no diálogo e na aba de "Previsões e metas". Encerrar para a geração depois do mês
    /// selecionado; remover um modelo já encerrado o tira da lista sem apagar o histórico.
    /// </summary>
    /// <param name="dono">Janela sobre a qual as confirmações abrem.</param>
    /// <param name="agir">Executa a ação com o tratamento de erros de quem chama (e, no diálogo, refaz a lista).</param>
    private Control LinhasDeRecorrencia(Func<Window> dono, Func<Func<Task>, Task> agir)
    {
        var fileiras = new StackPanel();
        foreach (var recorrencia in dados.Recorrencias.OrderBy(r => r.MesFinal is not null).ThenBy(r => r.Titulo))
        {
            if (fileiras.Children.Count > 0) fileiras.Children.Add(Divisoria());
            var detalhe = $"{recorrencia.Categoria} · todo dia {recorrencia.DiaDoMes} · {Dinheiro.Formatar(recorrencia.Valor)} · desde {recorrencia.MesInicial.ToString("MM/yyyy")}";
            var quem = Pilha(new TextBlock { Text = recorrencia.Titulo, FontWeight = FontWeight.SemiBold, FontSize = 13 }, Texto(detalhe, "muted").ComClasse("small")); quem.Spacing = 1; quem.VerticalAlignment = VerticalAlignment.Center; quem.Margin = new Thickness(12, 0);
            Control situacao = recorrencia.MesFinal is { } fim ? Etiqueta($"Encerrada em {fim:MM/yyyy}", "status") : Etiqueta("Ativa", "positive");
            situacao.Margin = new Thickness(0, 0, 12, 0);
            var id = recorrencia.Id;
            var acao = recorrencia.MesFinal is null
                ? Botao("Encerrar", () => _ = agir(async () =>
                {
                    // O mês selecionado na janela principal vira a última cobrança; os meses em aberto depois dele saem.
                    var ultimo = mes.ToString("MMMM 'de' yyyy", Dinheiro.Cultura);
                    if (await Confirmar(dono(), "Encerrar recorrência?", $"Encerrar “{recorrencia.Titulo}”? A última cobrança será em {ultimo} (o mês selecionado). Meses em aberto depois dele são removidos; o que já foi pago continua no histórico.")) Gravar(d => MotorDeRecorrencias.Encerrar(d, id, mes));
                }))
                : Botao("Remover", () => _ = agir(async () =>
                {
                    if (await Confirmar(dono(), "Remover recorrência?", $"Remover “{recorrencia.Titulo}” da lista? Os lançamentos já criados continuam como despesas comuns.")) Gravar(d => MotorDeRecorrencias.Remover(d, id));
                }), "ghost danger");
            var fileira = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(0, 10) };
            Grid.SetColumn(quem, 1); Grid.SetColumn(situacao, 2); Grid.SetColumn(acao, 3); fileira.Children.Add(CirculoComIcone(NomeDoIconeDaCategoria(recorrencia.Categoria))); fileira.Children.Add(quem); fileira.Children.Add(situacao); fileira.Children.Add(acao); fileiras.Children.Add(fileira);
        }
        if (dados.Recorrencias.Count == 0) fileiras.Children.Add(Texto("Nenhuma despesa recorrente ainda. Ao criar uma despesa, marque “Repetir todo mês”.", "muted"));
        return fileiras;
    }
    /// <summary>
    /// Abre a lista de despesas que se repetem todo mês em um diálogo (atalho a partir da página de despesas).
    /// </summary>
    private async Task GerenciarRecorrencias()
    {
        var suporte = new ContentControl(); var erro = Texto("", "error");
        Window? dialogo = null;
        // Executa a ação, mostra um erro simples no próprio diálogo e atualiza a lista.
        async Task Agir(Func<Task> acao)
        {
            try { erro.Text = ""; await acao(); }
            catch (Exception ex) { erro.Text = ErroAmigavel(ex); }
            suporte.Content = LinhasDeRecorrencia(() => dialogo!, Agir);
        }
        suporte.Content = LinhasDeRecorrencia(() => dialogo!, Agir);
        var painel = Pilha(Cabecalho("Despesas recorrentes", "Contas que se repetem todo mês. Cada mês é uma despesa comum: você pode pagar, editar ou excluir um mês sem afetar os outros."), suporte, erro);
        dialogo = Dialogo("Despesas recorrentes", painel, 720); dialogo.Height = 560;
        painel.Children.Add(Linha(Botao("Fechar", () => dialogo.Close(), "primary")));
        await dialogo.ShowDialog(this);
    }
}
