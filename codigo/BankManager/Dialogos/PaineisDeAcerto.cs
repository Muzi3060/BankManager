using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using BankManager.Nucleo;
using static BankManager.Componentes;

namespace BankManager;
// Quem pagou e acerto de contas: a pergunta "Quem pagou?", o cartão da visão geral, o registro de acertos e o histórico.
public sealed partial class JanelaPrincipal
{
    /// <summary>
    /// Nome de uma pessoa pelo identificador; um registro órfão (não deveria existir) aparece como "—".
    /// </summary>
    private string NomeDe(Guid id) => dados.Pessoas.FirstOrDefault(p => p.Id == id)?.Nome ?? "—";
    /// <summary>
    /// Pergunta quem pagou uma despesa e em que dia (padrão: hoje). Com uma só pessoa cadastrada não pergunta: ela pagou hoje, e a
    /// data pode ser corrigida ao editar a despesa. Em despesa individual, o responsável vem destacado.
    /// </summary>
    /// <returns>Se o usuário escolheu (falso ao cancelar), a pessoa (nula em "Não informar") e a data do pagamento.</returns>
    private async Task<(bool Escolheu, Guid? Pagador, DateOnly DataDoPagamento)> PerguntarQuemPagou(Despesa despesa)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        if (dados.Pessoas.Count == 1) return (true, dados.Pessoas[0].Id, hoje);
        var resultado = (Escolheu: false, Pagador: (Guid?)null, DataDoPagamento: hoje);
        Window? dialogo = null; var erro = Texto("", "error");
        var data = CampoDeTexto(hoje.ToString("dd/MM/yyyy"), "dd/mm/aaaa", 10);
        // Só fecha com uma data válida; caso contrário, mostra o erro e deixa corrigir.
        void Escolher(Guid? pagador)
        {
            try { resultado = (true, pagador, InterpretarData(data.Text, "Data do pagamento")); dialogo!.Close(); }
            catch (Exception ex) { erro.Text = ErroAmigavel(ex); }
        }
        var sugerido = despesa.Divisao == ModoDeDivisao.Individual && despesa.Participantes.Count == 1 ? despesa.Participantes[0] : (Guid?)null;
        var painel = Pilha(Cabecalho("Quem pagou?", $"{despesa.Titulo} · {Dinheiro.Formatar(despesa.Valor)}"), Campo("Data do pagamento (dd/mm/aaaa)", data), erro);
        foreach (var pessoa in dados.Pessoas)
        {
            var id = pessoa.Id; var botao = Botao(pessoa.Nome, () => Escolher(id), id == sugerido ? "primary" : null); botao.HorizontalAlignment = HorizontalAlignment.Stretch; painel.Children.Add(botao);
        }
        var nenhum = Botao("Não informar", () => Escolher(null), "ghost"); nenhum.HorizontalAlignment = HorizontalAlignment.Stretch; painel.Children.Add(nenhum);
        painel.Children.Add(Texto("Quem pagou entra no acerto de contas: quem não pagou deve a sua parte a quem pagou. Sem informar, a despesa fica fora do acerto.", "muted"));
        painel.Children.Add(Botao("Cancelar", () => dialogo!.Close(), "ghost"));
        dialogo = Dialogo("Quem pagou?", painel, 460); dialogo.Height = Math.Min(760, 440 + 50 * dados.Pessoas.Count);
        await dialogo.ShowDialog(this);
        return resultado;
    }
    /// <summary>
    /// Cartão "Acerto de contas": quem deve a quem (saldo acumulado de todos os meses), com os avatares das duas pessoas
    /// e a ação de registrar um acerto. Sem dívidas, mostra "Tudo certo por aqui".
    /// </summary>
    /// <param name="historico">Mostra o link "Histórico" (a aba de acerto já traz o histórico na própria página).</param>
    private Control CartaoDeAcerto(bool historico = true)
    {
        var dividas = MotorDeAcertos.Dividas(dados);
        var painel = Pilha(TituloDeSecao("Acerto de contas", historico && dados.Acertos.Count > 0 ? Botao("Histórico", () => _ = Executar(MostrarAcertos), "link") : null)); painel.Spacing = 10;
        if (dividas.Count == 0)
        {
            var algumPagador = dados.Acertos.Count > 0 || dados.Despesas.Any(e => e.Paga && e.PagaPor is not null);
            var vazio = Pilha(Texto("Tudo certo por aqui", "strong"), Texto(algumPagador ? "Ninguém deve nada a ninguém." : "Ao marcar uma despesa como paga, informe quem pagou. O BankManager mostra aqui quem deve a quem.", "muted")); vazio.Spacing = 4;
            painel.Children.Add(vazio);
        }
        foreach (var divida in dividas)
        {
            if (divida != dividas[0]) painel.Children.Add(Divisoria());
            var (devedor, credor) = (NomeDe(divida.IdDoDevedor), NomeDe(divida.IdDoCredor));
            // Avatares com uma seta entre eles: quem deve (neutro) paga a quem recebe (em destaque).
            var seta = Icone("ArrowRight", 18); seta.Margin = new Thickness(12, 0);
            var avatares = new StackPanel { Orientation = Orientation.Horizontal }; avatares.Children.Add(Avatar(devedor).ComClasse("large")); avatares.Children.Add(seta); avatares.Children.Add(Avatar(credor, forte: true).ComClasse("large"));
            // Só o valor em negrito; o nome de automação traz a frase completa (leitores de tela e testes).
            var frase = new TextBlock { TextWrapping = TextWrapping.Wrap };
            frase.Inlines!.Add(new Run(devedor + " deve ")); frase.Inlines.Add(new Run(Dinheiro.Formatar(divida.Valor)) { FontWeight = FontWeight.SemiBold }); frase.Inlines.Add(new Run(" a " + credor));
            AutomationProperties.SetName(frase, $"{devedor} deve {Dinheiro.Formatar(divida.Valor)} a {credor}");
            var acertar = Botao("Registrar acerto", () => _ = Executar(() => RegistrarAcerto(divida)), "ink"); acertar.HorizontalAlignment = HorizontalAlignment.Left;
            var bloco = Pilha(avatares, frase, acertar); bloco.Spacing = 12; acertar.Margin = new Thickness(0, 6, 0, 0); painel.Children.Add(bloco);
        }
        if (dividas.Count > 0) painel.Children.Add(Texto("Saldo acumulado de todos os meses, já descontados os acertos registrados.", "muted").ComClasse("small"));
        return Cartao(painel);
    }
    /// <summary>
    /// Registra um pagamento entre duas pessoas. Parte de uma dívida (quem deve paga a quem recebe, no valor devido, editável para pagamentos parciais).
    /// </summary>
    private async Task RegistrarAcerto(Divida? sugestao = null)
    {
        var nomes = dados.Pessoas.Select(p => p.Nome).ToList();
        var de = ListaDeOpcoes(nomes, sugestao is null ? 0 : dados.Pessoas.FindIndex(p => p.Id == sugestao.IdDoDevedor));
        var para = ListaDeOpcoes(nomes, sugestao is null ? Math.Min(1, nomes.Count - 1) : dados.Pessoas.FindIndex(p => p.Id == sugestao.IdDoCredor));
        var valor = CampoDeTexto(sugestao?.Valor.ToString("0.00", Dinheiro.Cultura), "Ex.: 200,00", 12);
        var data = CampoDeTexto(DateTime.Today.ToString("dd/MM/yyyy"), "dd/mm/aaaa", 10);
        var erro = Texto("", "error");
        var painel = Pilha(Cabecalho("Registrar acerto", "Um pagamento entre duas pessoas para quitar o que uma deve à outra."),
            Colunas(Campo("Quem pagou", de), Campo("Quem recebeu", para)), Colunas(Campo("Valor (R$)", valor, "Pode ser menor que o devido."), Campo("Data (dd/mm/aaaa)", data)), erro);
        var dialogo = Dialogo("Registrar acerto", painel, 600); dialogo.Height = 480;
        painel.Children.Add(Linha(Botao("Cancelar", () => dialogo.Close()), Botao("Salvar acerto", () =>
        {
            try
            {
                if (de.SelectedIndex == para.SelectedIndex) throw new ErroDeOrcamento("Escolha duas pessoas diferentes.");
                if (!DateOnly.TryParseExact(data.Text, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia)) throw new ErroDeOrcamento("Data: informe uma data válida no formato dd/mm/aaaa.");
                var acerto = new Acerto { IdDoPagador = dados.Pessoas[de.SelectedIndex].Id, IdDoRecebedor = dados.Pessoas[para.SelectedIndex].Id, Valor = Dinheiro.Interpretar(valor.Text, "Valor", true), Data = dia };
                Gravar(d => d.Acertos.Add(acerto)); dialogo.Close();
            }
            catch (Exception ex) { erro.Text = ErroAmigavel(ex); }
        }, "primary")));
        await dialogo.ShowDialog(this);
    }
    /// <summary>
    /// Linhas do histórico de acertos, do mais recente ao mais antigo, com a opção de excluir um registrado por engano (a dívida volta).
    /// Usadas no diálogo de histórico e na aba de acerto de "Previsões e metas".
    /// </summary>
    /// <param name="dono">Janela sobre a qual a confirmação abre.</param>
    /// <param name="depois">Chamado depois de uma exclusão, para o diálogo refazer a lista (a página se refaz sozinha).</param>
    private Control LinhasDeAcerto(Func<Window> dono, Action depois)
    {
        var fileiras = new StackPanel();
        foreach (var s in dados.Acertos.OrderByDescending(x => x.Data))
        {
            if (fileiras.Children.Count > 0) fileiras.Children.Add(Divisoria());
            var id = s.Id;
            var texto = Texto($"{s.Data:dd/MM/yyyy} · {NomeDe(s.IdDoPagador)} pagou a {NomeDe(s.IdDoRecebedor)}"); texto.VerticalAlignment = VerticalAlignment.Center;
            var valor = Texto(Dinheiro.Formatar(s.Valor), "strong"); valor.VerticalAlignment = VerticalAlignment.Center; valor.Margin = new Thickness(16, 0);
            var excluir = Botao("Excluir acerto", () => _ = Executar(async () =>
            {
                if (await Confirmar(dono(), "Excluir acerto?", "O acerto sai do histórico e a dívida correspondente volta a aparecer.")) Gravar(d => d.Acertos.RemoveAll(x => x.Id == id));
                depois();
            }), "ghost round danger", "Trash", soIcone: true);
            var fileira = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 6) };
            Grid.SetColumn(valor, 1); Grid.SetColumn(excluir, 2); fileira.Children.Add(texto); fileira.Children.Add(valor); fileira.Children.Add(excluir); fileiras.Children.Add(fileira);
        }
        if (dados.Acertos.Count == 0) fileiras.Children.Add(Texto("Nenhum acerto registrado ainda.", "muted"));
        return fileiras;
    }
    /// <summary>
    /// Abre o histórico de acertos em um diálogo (atalho a partir do cartão da visão geral).
    /// </summary>
    private async Task MostrarAcertos()
    {
        var suporte = new ContentControl(); Window? dialogo = null;
        void Atualizar() => suporte.Content = LinhasDeAcerto(() => dialogo!, Atualizar);
        Atualizar();
        var painel = Pilha(Cabecalho("Histórico de acertos", "Pagamentos já feitos entre as pessoas."), suporte);
        dialogo = Dialogo("Histórico de acertos", painel, 640); dialogo.Height = 520;
        painel.Children.Add(Linha(Botao("Fechar", () => dialogo.Close(), "primary")));
        await dialogo.ShowDialog(this);
    }
}
