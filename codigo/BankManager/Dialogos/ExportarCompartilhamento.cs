using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BankManager.Nucleo;
using static BankManager.Componentes;

namespace BankManager;
// Compartilhar despesas, parte de quem envia: o diálogo que mostra o que vai no arquivo e o salva. A escolha das despesas é feita
// antes, no modo de seleção da página Despesas (veja PaginaDeDespesas.cs).
public sealed partial class JanelaPrincipal
{
    private static readonly FilePickerFileType ArquivosDeCompartilhamento = new("Despesas compartilhadas do BankManager") { Patterns = ["*" + ArquivoDeCompartilhamento.Extensao] };
    // Versão curta do aplicativo ("2.2"), gravada no arquivo para informar quem recebe.
    private static string RotuloDaVersao => typeof(JanelaPrincipal).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}" : "";
    /// <summary>
    /// Diálogo "Compartilhar despesas": parte das despesas marcadas na lista e mostra quantas vão, o total e as pessoas citadas.
    /// Duas opções, desligadas por padrão: incluir as outras parcelas de uma compra parcelada e incluir a situação de pagamento.
    /// "Salvar arquivo" abre o seletor do sistema; depois de salvar, o modo de seleção termina.
    /// </summary>
    private void AbrirExportacao()
    {
        var escolhidas = dados.Despesas.Where(e => idsSelecionados.Contains(e.Id)).ToList();
        if (escolhidas.Count == 0 || SobreposicaoAberta) return;
        var nomes = dados.Pessoas.ToDictionary(p => p.Id, p => p.Nome);
        // Para cada compra parcelada com alguma parcela marcada: as parcelas que ficaram de fora.
        var grupos = escolhidas.Where(e => e.GrupoDeParcelas is not null).GroupBy(e => e.GrupoDeParcelas!.Value)
            .Select(g => (Titulo: g.First().Titulo, Marcadas: g.Count(), Outras: dados.Despesas.Where(e => e.GrupoDeParcelas == g.Key && !idsSelecionados.Contains(e.Id)).ToList())).Where(g => g.Outras.Count > 0).ToList();
        // Caixa de marcação com uma linha de explicação embaixo, que muda conforme ligada ou desligada.
        (CheckBox Verificar, Control Visao) Opcao(string rotulo, string desligado, string ligado, Action mudou)
        {
            var caixaDeMarcar = new CheckBox { Content = new TextBlock { Text = rotulo, FontWeight = FontWeight.SemiBold }, IsChecked = false }; AutomationProperties.SetName(caixaDeMarcar, rotulo);
            var dica = Texto("Desligado: " + desligado, "apagado"); dica.Classes.Add("pequeno"); dica.Margin = new Thickness(28, 0, 0, 0);
            caixaDeMarcar.IsCheckedChanged += (_, _) => { dica.Text = caixaDeMarcar.IsChecked == true ? "Ligado: " + ligado : "Desligado: " + desligado; mudou(); };
            var visao = Pilha(caixaDeMarcar, dica); visao.Spacing = 0; return (caixaDeMarcar, visao);
        }
        var quantidade = Texto("", "indicador"); var total = Texto("", "indicador"); var pessoas = Texto("", "indicador"); pessoas.TextTrimming = TextTrimming.CharacterEllipsis; pessoas.TextWrapping = TextWrapping.NoWrap;
        var lista = new StackPanel(); var erro = Texto("", "erro"); erro.IsVisible = false;
        var opcoesDeGrupo = new List<(CheckBox Verificar, List<Despesa> Outras)>(); CheckBox? pagamento = null;
        // Despesas que vão no arquivo: as marcadas, mais as outras parcelas das compras cuja opção foi ligada.
        List<Despesa> Efetivas() => escolhidas.Concat(opcoesDeGrupo.Where(g => g.Verificar.IsChecked == true).SelectMany(g => g.Outras)).OrderBy(e => e.Vencimento).ThenBy(e => e.Titulo).ToList();
        // Indicadores e lista acompanham as opções.
        void Recalcular()
        {
            var itens = Efetivas(); var comPagamento = pagamento?.IsChecked == true;
            quantidade.Text = itens.Count.ToString(); total.Text = Dinheiro.Formatar(itens.Sum(e => e.Valor));
            pessoas.Text = string.Join(", ", itens.SelectMany(e => comPagamento && e.PagaPor is { } p ? e.Participantes.Append(p) : e.Participantes).Distinct().Select(id => nomes[id])); ToolTip.SetTip(pessoas, pessoas.Text);
            lista.Children.Clear();
            foreach (var e in itens)
            {
                if (lista.Children.Count > 0) lista.Children.Add(Divisoria());
                var categoria = e.Categoria + (dados.CategoriasProprias.Contains(e.Categoria) ? " (categoria sua)" : "");
                var detalhe = categoria + (e.TotalDeParcelas > 1 ? $" · parcela {e.NumeroDaParcela} de {e.TotalDeParcelas}" : e.IdDaRecorrencia is not null ? " · recorrente" : "") + $" · {e.Vencimento:dd/MM}" + (e.Vencimento.Year != mes.Year ? $"/{e.Vencimento:yy}" : "");
                var titulo = Pilha(new TextBlock { Text = e.Titulo, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis }, Texto(detalhe, "apagado").ComClasse("pequeno")); titulo.Spacing = 1;
                var valor = Texto(Dinheiro.Formatar(e.Valor), "forte"); valor.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(valor, 1);
                lista.Children.Add(new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 8), Children = { titulo, valor } });
            }
        }
        // Faixa de três indicadores, separados por linhas finas.
        Control Indicador(string rotulo, TextBlock valorLido, int coluna) { valorLido.FontSize = 22; var indicador = Pilha(Texto(rotulo, "apagado").ComClasse("pequeno"), valorLido); indicador.Spacing = 2; indicador.Margin = new Thickness(coluna == 0 ? 0 : 18, 0, 18, 0); Grid.SetColumn(indicador, coluna * 2); return indicador; }
        var indicadores = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*,Auto,1.3*") };
        foreach (var (rotulo, valorLido, coluna) in new[] { ("Despesas", quantidade, 0), ("Total", total, 1), ("Pessoas citadas", pessoas, 2) }) indicadores.Children.Add(Indicador(rotulo, valorLido, coluna));
        foreach (var coluna in new[] { 1, 3 }) { var linha = new Border { Width = 1, Height = double.NaN }.ComClasse("divisoria"); Grid.SetColumn(linha, coluna); indicadores.Children.Add(linha); }
        var corpo = Pilha(new Border { Child = indicadores }.ComClasse("painel"), new ScrollViewer { Content = lista, MaxHeight = 210 }); corpo.Spacing = 14;
        foreach (var grupo in grupos)
        {
            var n = grupo.Outras.Count; var qual = grupo.Marcadas == 1 ? "vai só a parcela marcada." : $"vão só as {grupo.Marcadas} parcelas marcadas.";
            var opcao = Opcao($"Incluir {(n == 1 ? "a outra parcela" : $"as outras {n} parcelas")} de {grupo.Titulo}", qual, $"vão todas as parcelas de {grupo.Titulo}.", Recalcular);
            opcoesDeGrupo.Add((opcao.Verificar, grupo.Outras)); corpo.Children.Add(opcao.Visao);
        }
        var opcaoDePagamento = Opcao("Incluir situação de pagamento e quem pagou", "a outra pessoa recebe tudo como “em aberto”.", "vão também pago/em aberto, a data do pagamento e quem pagou.", Recalcular);
        pagamento = opcaoDePagamento.Verificar; corpo.Children.Add(opcaoDePagamento.Visao);
        var naoVai = new TextBlock(); naoVai.Inlines!.Add(new Avalonia.Controls.Documents.Run("Não vai no arquivo: ") { FontWeight = FontWeight.SemiBold }); naoVai.Inlines.Add(new Avalonia.Controls.Documents.Run("rendas, metas, acertos de contas e as outras despesas."));
        corpo.Children.Add(Faixa("Cadeado", naoVai)); corpo.Children.Add(Faixa("Alerta", "O arquivo é texto legível, sem senha. Envie só para quem deve receber.", "alerta")); corpo.Children.Add(erro);
        // Nome sugerido: despesas-<mês abreviado>-<ano>.bmshare.json, com o mês selecionado na janela.
        var sugerido = $"despesas-{mes.ToString("MMM", Dinheiro.Cultura).TrimEnd('.').ToLower(Dinheiro.Cultura)}-{mes.Year}{ArquivoDeCompartilhamento.Extensao}";
        var nomeDoArquivo = Pilha(Texto("Nome do arquivo", "apagado").ComClasse("pequeno"), Texto(sugerido, "mono")); nomeDoArquivo.Spacing = 2;
        var salvar = Botao("Salvar arquivo", () => { }, "primario", "Baixar");
        salvar.Click += async (_, _) =>
        {
            try
            {
                erro.IsVisible = false;
                var arquivo = CompartilhamentoDeDespesas.Montar(dados, Efetivas().Select(e => e.Id).ToList(), pagamento.IsChecked == true, RotuloDaVersao);
                var caminho = CaminhoDeTesteParaSalvar;
                if (caminho is null)
                {
                    var destino = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Salvar despesas compartilhadas", SuggestedFileName = sugerido, FileTypeChoices = [ArquivosDeCompartilhamento] });
                    if (destino is null) return;
                    caminho = destino.TryGetLocalPath() ?? throw new ErroDeOrcamento("Escolha uma pasta local.");
                    // O seletor pode devolver o nome sem a extensão própria; ela identifica o tipo do arquivo.
                    if (!caminho.EndsWith(ArquivoDeCompartilhamento.Extensao, StringComparison.OrdinalIgnoreCase)) caminho += ArquivoDeCompartilhamento.Extensao;
                }
                RecusarCaminhoInterno(caminho); CompartilhamentoDeDespesas.Salvar(caminho, arquivo);
                // Confirmação com o local do arquivo; a seleção termina porque o trabalho dela acabou.
                FecharSobreposicao(); EncerrarSelecao();
                Avisar($"{arquivo.Despesas.Count} {(arquivo.Despesas.Count == 1 ? "despesa salva" : "despesas salvas")} em {caminho}. Envie o arquivo só para quem deve recebê-lo.", sucesso: true);
            }
            catch (Exception ex) { erro.Text = ErroAmigavel(ex); erro.IsVisible = true; }
        };
        // Cancelar, o "X" e a tecla Esc fecham o diálogo e mantêm a seleção, para a pessoa poder ajustá-la.
        var (cartao, tituloDaSecao) = CartaoModal("Compartilhar despesas", "Gera um arquivo que outra pessoa pode importar sem perder os dados dela.", corpo, RodapeModal(nomeDoArquivo, Botao("Cancelar", FecharSobreposicao), salvar), FecharSobreposicao);
        Recalcular(); EsconderAviso(); MostrarSobreposicao(cartao, FecharSobreposicao, tituloDaSecao);
    }
}
