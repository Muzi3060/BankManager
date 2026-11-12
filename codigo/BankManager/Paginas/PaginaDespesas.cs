using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using BankManager.Nucleo;
using static BankManager.Componentes;

namespace BankManager;
// Página "Despesas": lista em linhas com filtros e menu de ações, mais o editor de despesas (compra, itens e parcelas).
public sealed partial class JanelaPrincipal
{
    // Larguras das colunas da lista: descrição (flexível), vencimento, divisão, situação, valor e menu "⋯".
    private const string ColunasDeDespesa = "*,84,170,140,120,48";
    // No modo de seleção, a primeira coluna é a caixa de marcação e o menu "⋯" some.
    private const string ColunasDeSelecao = "40,*,84,170,140,120";
    // Máximo de linhas desenhadas ao buscar em todos os meses; o resumo continua contando todas as encontradas.
    private const int MaximoDeLinhas = 200;
    /// <summary>
    /// Monta o cabeçalho, a busca e os filtros; ao mudar um filtro, só as linhas são reconstruídas (o campo de busca mantém o foco).
    /// O botão "Compartilhar" liga o modo de seleção: caixas de marcação na lista, faixa de atalhos e barra flutuante.
    /// </summary>
    private Control PaginaDeDespesas()
    {
        var busca = CampoDeTexto(filtro, "Buscar uma despesa…"); busca.InnerLeftContent = Icone("Search", 16); busca.Padding = new Thickness(12, 10, 12, 10);
        AutomationProperties.SetName(busca, "Buscar despesas");
        var categorias = RegrasDoOrcamento.TodasAsCategorias(dados); var categoria = ListaDeOpcoes(new[] { "Todas as categorias" }.Concat(categorias), filtroDeCategoria); categoria.MinWidth = 190; categoria.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(categoria, "Categoria");
        var resumo = Texto("", "muted"); var tabela = new Border(); tabela.Classes.Add("card"); tabela.Padding = new Thickness(0); tabela.ClipToBounds = true;
        // Despesas visíveis com os filtros atuais; os atalhos de seleção agem só sobre elas.
        List<Despesa> visiveis = [];
        // Função local: captura os filtros e refaz resumo e linhas, sem recriar a página inteira.
        void Atualizar()
        {
            var lancamentos = BuscaDeDespesas.Buscar(dados, todosOsMeses ? null : mes, filtro, filtroDeSituacao == 0 ? null : filtroDeSituacao == 2, filtroDeCategoria == 0 ? null : categorias[filtroDeCategoria - 1]);
            visiveis = lancamentos.Take(MaximoDeLinhas).ToList();
            resumo.Text = $"{lancamentos.Count} {(lancamentos.Count == 1 ? "despesa" : "despesas")} · Total {Dinheiro.Formatar(lancamentos.Sum(e => e.Valor))} · Em aberto {Dinheiro.Formatar(lancamentos.Where(e => !e.Paga).Sum(e => e.Valor))}" + (lancamentos.Count > MaximoDeLinhas ? $" · mostrando as {MaximoDeLinhas} mais recentes" : "");
            tabela.Child = lancamentos.Count == 0 ? DespesasVazias() : TabelaDeDespesas(visiveis);
        }
        busca.TextChanged += (_, _) => { filtro = busca.Text ?? ""; Atualizar(); };
        categoria.SelectionChanged += (_, _) => { filtroDeCategoria = categoria.SelectedIndex; Atualizar(); };
        var situacao = Segmentado(["Todas", "Em aberto", "Pagas"], filtroDeSituacao, i => { filtroDeSituacao = i; Atualizar(); });
        var compartilhar = Botao("Compartilhar", () => { if (selecionando) EncerrarSelecao(); else IniciarSelecao(); }, selecionando ? "toggle active" : "toggle", "Share");
        ToolTip.SetTip(compartilhar, selecionando ? "Sair do modo de seleção" : "Escolher despesas para enviar a outra pessoa");
        var filtros = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,Auto,12,Auto,12,Auto") }; Grid.SetColumn(situacao, 2); Grid.SetColumn(categoria, 4); Grid.SetColumn(compartilhar, 6);
        filtros.Children.Add(busca); filtros.Children.Add(situacao); filtros.Children.Add(categoria); filtros.Children.Add(compartilhar);
        Atualizar();
        // Resumo à esquerda e acesso às despesas que se repetem todo mês à direita.
        var recorrentes = Botao("Recorrentes", () => _ = Executar(GerenciarRecorrencias), "link", "Repeat"); resumo.VerticalAlignment = VerticalAlignment.Center;
        // Escopo da busca: o mês selecionado ou todos os meses (a navegação de mês some no modo geral).
        var escopo = Segmentado(["Este mês", "Todos os meses"], todosOsMeses ? 1 : 0, i => { todosOsMeses = i == 1; Desenhar(); });
        var ferramentas = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; ferramentas.Children.Add(escopo); ferramentas.Children.Add(recorrentes); Grid.SetColumn(ferramentas, 1);
        var linhaDoResumo = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; linhaDoResumo.Children.Add(resumo); linhaDoResumo.Children.Add(ferramentas);
        var pagina = Pilha(CabecalhoDaPagina("Despesas", todosOsMeses ? "Busca em todos os meses, do mais recente ao mais antigo." : "Contas, compras e parcelas do mês.", navegacaoDeMes: !todosOsMeses), filtros); pagina.Spacing = 16;
        if (selecionando)
        {
            // Faixa de atalhos. Eles marcam só o que está visível com a busca e os filtros atuais.
            var todas = Botao("Todas deste mês", () => { foreach (var e in visiveis.Where(e => Datas.Mes(e.Vencimento) == mes)) idsSelecionados.Add(e.Id); Atualizar(); AtualizarBarraDeSelecao(); }, "pillbtn");
            ToolTip.SetTip(todas, "Marca as despesas de " + mes.ToString("MMMM 'de' yyyy", Dinheiro.Cultura) + " que estão na lista");
            var porCategoria = Botao("Uma categoria…", () => { }, "pillbtn"); var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
            foreach (var nome in categorias)
            {
                var item = new MenuItem { Header = nome, Icon = Icone(NomeDoIconeDaCategoria(nome), 16) }; AutomationProperties.SetName(item, nome);
                item.Click += (_, _) => { foreach (var e in visiveis.Where(e => e.Categoria == nome)) idsSelecionados.Add(e.Id); Atualizar(); AtualizarBarraDeSelecao(); }; menu.Items.Add(item);
            }
            porCategoria.Flyout = menu; ToolTip.SetTip(porCategoria, "Marca as despesas de uma categoria que estão na lista");
            var limpar = Botao("Limpar seleção", () => { idsSelecionados.Clear(); Atualizar(); AtualizarBarraDeSelecao(); }, "pillbtn");
            var atalhos = Linha(todas, porCategoria, limpar); Grid.SetColumn(atalhos, 1);
            pagina.Children.Add(new Border { Child = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { Texto("Escolha as despesas que quer enviar."), atalhos } } }.ComClasse("selectbanner"));
        }
        pagina.Children.Add(linhaDoResumo); pagina.Children.Add(tabela);
        // Espaço no fim da página para a barra flutuante não cobrir a última linha.
        if (selecionando) pagina.Children.Add(new Border { Height = 64 });
        return pagina;
    }
    /// <summary>
    /// Liga o modo de seleção, começando sem nada marcado.
    /// </summary>
    private void IniciarSelecao() { selecionando = true; idsSelecionados.Clear(); Desenhar(); }
    /// <summary>
    /// Sai do modo de seleção e esquece o que estava marcado.
    /// </summary>
    private void EncerrarSelecao() { if (!selecionando) return; selecionando = false; idsSelecionados.Clear(); Desenhar(); }
    /// <summary>
    /// Atualiza a barra flutuante: quantas despesas estão marcadas e o total delas. Sem nenhuma, "Compartilhar…" fica desligado.
    /// </summary>
    private void AtualizarBarraDeSelecao()
    {
        barraDeSelecao.IsVisible = selecionando && pronto && paginaAtual == 2;
        // Despesas excluídas depois de marcadas saem da conta.
        var escolhidas = dados.Despesas.Where(e => idsSelecionados.Contains(e.Id)).ToList();
        textoDaQuantidade.Text = escolhidas.Count == 1 ? "1 selecionada" : $"{escolhidas.Count} selecionadas"; textoDoTotal.Text = "Total " + Dinheiro.Formatar(escolhidas.Sum(e => e.Valor));
        if (botaoCompartilhar is not null) botaoCompartilhar.IsEnabled = escolhidas.Count > 0;
    }
    /// <summary>
    /// Estado vazio amigável: explica por que a lista está vazia e o que fazer em seguida.
    /// </summary>
    private Control DespesasVazias()
    {
        var alguma = todosOsMeses ? dados.Despesas.Count > 0 : MotorFinanceiro.Despesas(dados, mes).Count > 0;
        var painel = Pilha(Texto(alguma ? "Nenhuma despesa encontrada" : "Nenhuma despesa neste mês", "section"), Texto(alguma ? "Tente outra busca ou mude os filtros." : "Registre uma conta, uma compra ou uma dívida parcelada para começar. Se alguém enviou despesas para você, importe o arquivo.", "muted"));
        painel.Spacing = 8; painel.HorizontalAlignment = HorizontalAlignment.Center; painel.Margin = new Thickness(24, 40);
        foreach (var filho in painel.Children.OfType<TextBlock>()) filho.TextAlignment = TextAlignment.Center;
        if (alguma)
        {
            var limpar = Botao("Limpar filtros", () => { filtro = ""; filtroDeSituacao = 0; filtroDeCategoria = 0; Desenhar(); }, "link"); limpar.HorizontalAlignment = HorizontalAlignment.Center; painel.Children.Add(limpar);
            // Quem não achou no mês talvez procure em outro: oferece ampliar a busca em um clique.
            if (!todosOsMeses && filtro.Length > 0) { var emTodoLugar = Botao("Buscar em todos os meses", () => { todosOsMeses = true; Desenhar(); }, "link"); emTodoLugar.HorizontalAlignment = HorizontalAlignment.Center; painel.Children.Add(emTodoLugar); }
        }
        else
        {
            // Quem recebeu um arquivo de outra pessoa pode começar por ele: as despesas são adicionadas, nada é substituído.
            var acoes = Linha(Botao("Nova despesa", () => _ = Executar(() => EditarDespesa()), "primary", "Plus"), Botao("Importar despesas compartilhadas", () => _ = Executar(ImportarCompartilhadas), icone: "Download")); acoes.HorizontalAlignment = HorizontalAlignment.Center; painel.Children.Add(acoes);
        }
        return painel;
    }
    /// <summary>
    /// Tabela de despesas em linhas: descrição, vencimento, divisão por pessoa, situação, valor e menu de ações
    /// (no modo de seleção, uma caixa de marcação no lugar do menu).
    /// </summary>
    private Control TabelaDeDespesas(IReadOnlyList<Despesa> lancamentos)
    {
        var fileiras = new StackPanel();
        var cabecalho = new Grid { ColumnDefinitions = new ColumnDefinitions(selecionando ? ColunasDeSelecao : ColunasDeDespesa), Margin = new Thickness(22, 14, 14, 12) };
        string[] titulos = ["Descrição", "Vence", "Divisão", "Situação", "Valor"];
        for (var i = 0; i < titulos.Length; i++) { var rotulo = Texto(titulos[i], "muted"); rotulo.FontSize = 12; rotulo.FontWeight = FontWeight.Medium; if (i == 4) rotulo.TextAlignment = TextAlignment.Right; Grid.SetColumn(rotulo, selecionando ? i + 1 : i); cabecalho.Children.Add(rotulo); }
        fileiras.Children.Add(cabecalho);
        foreach (var e in lancamentos) { fileiras.Children.Add(Divisoria()); fileiras.Children.Add(LinhaDaDespesa(e)); }
        return fileiras;
    }
    /// <summary>
    /// Uma linha da lista. As ações ficam no menu "⋯" para manter a linha limpa. No modo de seleção, o menu dá lugar a uma caixa de
    /// marcação: clicar em qualquer ponto da linha (ou a barra de espaço, com o foco na caixa) alterna a marcação.
    /// </summary>
    private Control LinhaDaDespesa(Despesa despesa)
    {
        var grade = new Grid { ColumnDefinitions = new ColumnDefinitions(selecionando ? ColunasDeSelecao : ColunasDeDespesa), Margin = new Thickness(22, 10, 14, 10) };
        var titulo = Pilha(new TextBlock { Text = despesa.Titulo, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis }, Texto(SubtituloDaDespesa(despesa, false), "muted").ComClasse("small")); titulo.Spacing = 1; titulo.VerticalAlignment = VerticalAlignment.Center; titulo.Margin = new Thickness(0, 0, 12, 0);
        var vencimento = Texto(todosOsMeses ? $"{despesa.Vencimento:dd/MM/yy}" : $"{despesa.Vencimento:dd/MM}", "support"); vencimento.VerticalAlignment = VerticalAlignment.Center;
        // Divisão sem o símbolo R$ (a coluna Valor já mostra a moeda), um nome por linha, números alinhados.
        var cotas = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var compartilhar in MotorFinanceiro.Ratear(dados, despesa).Valores) { var linha = Texto($"{dados.Pessoas.First(p => p.Id == compartilhar.Key).Nome} {compartilhar.Value.ToString("N2", Dinheiro.Cultura)}", "muted"); linha.FontSize = 12; cotas.Children.Add(linha); }
        var etiqueta = EtiquetaDeSituacao(despesa);
        var valor = Texto(Dinheiro.Formatar(despesa.Valor), "strong"); valor.TextAlignment = TextAlignment.Right; valor.VerticalAlignment = VerticalAlignment.Center;
        var fileira = new Border { Child = grade }.ComClasse("exrow");
        Control[] celulas;
        if (selecionando)
        {
            var caixaDeMarcar = new CheckBox { IsChecked = idsSelecionados.Contains(despesa.Id), VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 }; AutomationProperties.SetName(caixaDeMarcar, "Selecionar " + despesa.Titulo);
            fileira.Classes.Add("selectable"); fileira.Classes.Set("selected", caixaDeMarcar.IsChecked == true);
            caixaDeMarcar.IsCheckedChanged += (_, _) =>
            {
                if (caixaDeMarcar.IsChecked == true) idsSelecionados.Add(despesa.Id); else idsSelecionados.Remove(despesa.Id);
                fileira.Classes.Set("selected", caixaDeMarcar.IsChecked == true); AtualizarBarraDeSelecao();
            };
            // Clique fora da caixa também alterna; o clique na própria caixa já é tratado por ela.
            fileira.PointerPressed += (_, e) => { if (e.Source is Visual fonte && fonte.FindAncestorOfType<CheckBox>(true) is null) { caixaDeMarcar.IsChecked = caixaDeMarcar.IsChecked != true; e.Handled = true; } };
            valor.Margin = new Thickness(0, 0, 8, 0); celulas = [caixaDeMarcar, titulo, vencimento, cotas, etiqueta, valor];
        }
        else
        {
            var mais = Botao("Mais ações", () => { }, "ghost round", "More", soIcone: true); mais.Flyout = MenuDaDespesa(despesa); mais.HorizontalAlignment = HorizontalAlignment.Right;
            celulas = [titulo, vencimento, cotas, etiqueta, valor, mais];
        }
        for (var i = 0; i < celulas.Length; i++) { Grid.SetColumn(celulas[i], i); grade.Children.Add(celulas[i]); }
        return fileira;
    }
    /// <summary>
    /// Menu "⋯" de uma despesa: marcar como paga (ou em aberto), editar e excluir.
    /// </summary>
    private MenuFlyout MenuDaDespesa(Despesa despesa)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        menu.Items.Add(AcaoDeMenu(despesa.Paga ? "Marcar em aberto" : "Marcar como pago", "Check", () => _ = Executar(() => AlternarPagamento(despesa))));
        menu.Items.Add(AcaoDeMenu("Editar", "Edit", () => _ = Executar(() => EditarDespesa(despesa))));
        menu.Items.Add(AcaoDeMenu("Excluir", "Trash", () => _ = Executar(async () =>
        {
            var detalhe = despesa.IdDaRecorrencia is not null ? "Apenas este mês será removido e não será criado de novo; a despesa continua nos outros meses. Para parar de vez, use Recorrentes." : "Apenas este lançamento será removido; outras parcelas continuam no histórico.";
            // RemoverDespesa registra o mês como ignorado quando o lançamento vem de uma recorrência.
            if (await Confirmar(this, "Excluir despesa?", $"Excluir “{despesa.Titulo}” de {Dinheiro.Formatar(despesa.Valor)}? {detalhe}")) Gravar(d => MotorDeRecorrencias.RemoverDespesa(d, despesa.Id));
        }), perigo: true));
        return menu;
    }
    /// <summary>
    /// Item de menu com ícone. O nome de automação guarda o rótulo (usado por leitores de tela e testes); o item perigoso usa o tom negativo.
    /// </summary>
    private static MenuItem AcaoDeMenu(string texto, string icone, Action acao, bool perigo = false)
    {
        var item = new MenuItem { Header = perigo ? new TextBlock { Text = texto, Classes = { "negative" } } : texto, Icon = Icone(icone, 16) };
        if (perigo) item.Classes.Add("danger");
        AutomationProperties.SetName(item, texto); item.Click += (_, _) => acao(); return item;
    }
    /// <summary>
    /// Alterna a situação de pagamento. Ao marcar como paga, pergunta quem pagou (alimenta o acerto de contas) e grava a data do
    /// pagamento (hoje, ou a informada no diálogo); ao reabrir, esquece o pagador e a data.
    /// </summary>
    private async Task AlternarPagamento(Despesa despesa)
    {
        if (despesa.Paga) { Gravar(d => { var e = d.Despesas.First(x => x.Id == despesa.Id); e.Paga = false; e.PagaPor = null; e.DataDoPagamento = null; }); return; }
        var (ok, pagador, pagaEm) = await PerguntarQuemPagou(despesa); if (!ok) return;
        Gravar(d => { var e = d.Despesas.First(x => x.Id == despesa.Id); e.Paga = true; e.PagaPor = pagador; e.DataDoPagamento = pagaEm; });
    }
    /// <summary>
    /// Lê uma data digitada no formato dd/mm/aaaa e recusa datas inválidas ou fora do intervalo aceito pelo aplicativo.
    /// </summary>
    private static DateOnly InterpretarData(string? texto, string rotulo)
    {
        if (!DateOnly.TryParseExact(texto?.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)) throw new ErroDeOrcamento($"{rotulo}: informe uma data válida no formato dd/mm/aaaa.");
        RegrasDoOrcamento.Data(data); return data;
    }
    /// <summary>
    /// Linha de apoio sob o título. Na lista: categoria e parcela ("parcela 3 de 10") ou "mensal". Na forma curta (visão geral):
    /// "Moradia · 12/11" ou "Parcela 3 de 10 · 15/11", com o vencimento.
    /// </summary>
    private static string SubtituloDaDespesa(Despesa despesa, bool comVencimento) => comVencimento
        ? (despesa.TotalDeParcelas > 1 ? $"Parcela {despesa.NumeroDaParcela} de {despesa.TotalDeParcelas}" : despesa.Categoria) + $" · {despesa.Vencimento:dd/MM}"
        : despesa.Categoria + (despesa.TotalDeParcelas > 1 ? $" · parcela {despesa.NumeroDaParcela} de {despesa.TotalDeParcelas}" : despesa.IdDaRecorrencia is not null ? " · recorrente" : "");
    /// <summary>
    /// Chip de situação da lista: "Pago em dd/MM" (ou só "Pago", quando a data do pagamento não foi informada), Em aberto,
    /// Vence em N dias ou Em atraso.
    /// </summary>
    private Border EtiquetaDeSituacao(Despesa despesa)
    {
        var s = SituacaoDoPrazo.De(despesa, DateOnly.FromDateTime(DateTime.Today));
        return s.Tipo switch
        {
            TipoDePrazo.Paga => Etiqueta(despesa.DataDoPagamento is { } pagaEm ? "Pago em " + pagaEm.ToString(todosOsMeses ? "dd/MM/yy" : "dd/MM") : "Pago", "positive"),
            TipoDePrazo.Atrasada => Etiqueta("Em atraso", "negative"),
            TipoDePrazo.VenceEmBreve => Etiqueta(s.Dias == 0 ? "Vence hoje" : $"Vence em {s.Dias} {(s.Dias == 1 ? "dia" : "dias")}", "warn"),
            _ => Etiqueta("Em aberto", "status")
        };
    }
    /// <summary>
    /// Chip de prazo dos próximos pagamentos: "Em 1 dia", "Em 7 dias", "Hoje" ou "Atrasada há N dias".
    /// </summary>
    private static Border EtiquetaDePrazo(Despesa despesa)
    {
        var s = SituacaoDoPrazo.De(despesa, DateOnly.FromDateTime(DateTime.Today)); var dias = Math.Abs(s.Dias); var unidade = dias == 1 ? "dia" : "dias";
        return s.Tipo switch
        {
            TipoDePrazo.Atrasada => Etiqueta($"Atrasada há {dias} {unidade}", "negative"),
            TipoDePrazo.VenceEmBreve => Etiqueta(s.Dias == 0 ? "Hoje" : $"Em {dias} {unidade}", "warn"),
            _ => Etiqueta($"Em {dias} {unidade}")
        };
    }
    /// <summary>
    /// Abre o editor com um rascunho local, itens e responsáveis; prévia e salvamento compartilham a mesma validação.
    /// </summary>
    private async Task EditarDespesa(Despesa? existente = null)
    {
        if (dados.Pessoas.Count == 0) { paginaAtual = 1; Desenhar(); throw new ErroDeOrcamento("Cadastre pelo menos uma pessoa antes de adicionar despesas."); }
        var titulo = CampoDeTexto(existente?.Titulo, "Ex.: Compra do mercado", 120);
        var categorias = RegrasDoOrcamento.TodasAsCategorias(dados); var categoria = ListaDeOpcoes(categorias, existente is null ? 1 : categorias.IndexOf(existente.Categoria));
        var data = CampoDeTexto((existente?.Vencimento ?? (mes == Datas.Mes(DateOnly.FromDateTime(DateTime.Today)) ? DateOnly.FromDateTime(DateTime.Today) : mes)).ToString("dd/MM/yyyy"), "dd/mm/aaaa", 10);
        var valor = CampoDeTexto(existente?.Valor.ToString("0.00", Dinheiro.Cultura), "Ex.: 1000,00", 12);
        var parcelas = CampoDeTexto((existente?.TotalDeParcelas ?? 1).ToString(), "1", 3); parcelas.IsEnabled = existente is null;
        var divisao = ListaDeOpcoes(["Individual · uma pessoa", "Igual · partes iguais", "Proporcional · renda disponível"], (int)(existente?.Divisao ?? (dados.Pessoas.Count == 1 ? ModoDeDivisao.Individual : ModoDeDivisao.RendaDisponivel)));
        var paga = new CheckBox { Content = "Esta despesa já foi paga", IsChecked = existente?.Paga ?? false };
        // "Quem pagou" só aparece com a despesa marcada como paga. Com uma única pessoa cadastrada, ela é o padrão.
        var pagador = ListaDeOpcoes(new[] { "Não informar" }.Concat(dados.Pessoas.Select(p => p.Nome)), existente?.PagaPor is { } conhecidas ? dados.Pessoas.FindIndex(p => p.Id == conhecidas) + 1 : dados.Pessoas.Count == 1 ? 1 : 0);
        // Data do pagamento: hoje ao marcar como paga; vazia em despesas pagas antes de essa informação existir (vale o vencimento).
        var dataDoPagamento = CampoDeTexto(existente?.DataDoPagamento?.ToString("dd/MM/yyyy"), "dd/mm/aaaa", 10);
        var campoDoPagador = Colunas(Campo("Quem pagou", pagador, "Sem informar, a despesa não entra no acerto de contas."), Campo("Data do pagamento (dd/mm/aaaa)", dataDoPagamento, "Em branco, o vencimento é usado como referência.")); campoDoPagador.IsVisible = paga.IsChecked == true;
        var participantes = dados.Pessoas.Select((p, i) => (Pessoa: p, Verificar: new CheckBox { Content = p.Nome, IsChecked = existente?.Participantes.Contains(p.Id) ?? true })).ToList();
        // No modo individual, as caixas funcionam como uma escolha exclusiva; nos demais, permitem várias pessoas.
        foreach (var participante in participantes)
            participante.Verificar.IsCheckedChanged += (_, _) =>
            {
                if (divisao.SelectedIndex == 0 && participante.Verificar.IsChecked == true)
                    foreach (var outra in participantes.Where(p => p.Pessoa.Id != participante.Pessoa.Id)) outra.Verificar.IsChecked = false;
            };
        divisao.SelectionChanged += (_, _) =>
        {
            if (divisao.SelectedIndex == 0)
            {
                var primeiro = participantes.FirstOrDefault(p => p.Verificar.IsChecked == true).Verificar ?? participantes[0].Verificar;
                foreach (var p in participantes) p.Verificar.IsChecked = p.Verificar == primeiro;
            }
        };
        // Ao marcar como paga uma despesa de uma só pessoa responsável, sugere essa pessoa como quem pagou.
        paga.IsCheckedChanged += (_, _) =>
        {
            campoDoPagador.IsVisible = paga.IsChecked == true;
            if (paga.IsChecked == true && string.IsNullOrWhiteSpace(dataDoPagamento.Text) && existente?.Paga != true) dataDoPagamento.Text = DateTime.Today.ToString("dd/MM/yyyy");
            var escolhidas = participantes.Where(p => p.Verificar.IsChecked == true).ToList();
            if (paga.IsChecked == true && pagador.SelectedIndex == 0 && escolhidas.Count == 1) pagador.SelectedIndex = dados.Pessoas.FindIndex(p => p.Id == escolhidas[0].Pessoa.Id) + 1;
        };
        var painelDePessoas = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var p in participantes) { p.Verificar.Margin = new Thickness(0, 0, 20, 6); painelDePessoas.Children.Add(p.Verificar); }
        var listaDeItens = new StackPanel { Spacing = 8 };
        // Copia os itens do lançamento para que remover/adicionar no formulário não altere o histórico ao cancelar.
        var itens = existente?.Itens.Select(i => i with { }).ToList() ?? [];
        var nomeDoItem = CampoDeTexto(null, "Nome do item", 100); var quantidadeDoItem = CampoDeTexto("1", "Quantidade", 10); var preco = CampoDeTexto(null, "Preço (R$)", 12);
        var totalDosItens = Texto("", "muted");
        var erro = Texto("", "error"); 
        var previa = Texto("", "muted");
        // Reconstrói itens e total do rascunho. Com itens, o valor manual fica bloqueado; a prévia antiga é descartada.
        void AtualizarItens()
        {
            listaDeItens.Children.Clear();
            foreach (var item in itens)
            {
                var linha = new DockPanel(); var remover = Botao("Remover", () => { itens.Remove(item); AtualizarItens(); }); DockPanel.SetDock(remover, Dock.Right); linha.Children.Add(remover);
                linha.Children.Add(Texto($"{item.Nome} · {item.Quantidade.ToString(Dinheiro.Cultura)} × {Dinheiro.Formatar(item.PrecoUnitario)} = {Dinheiro.Formatar(item.Total)}")); listaDeItens.Children.Add(linha);
            }
            valor.IsEnabled = itens.Count == 0;
            if (itens.Count > 0) valor.Text = itens.Sum(i => i.Total).ToString("0.00", Dinheiro.Cultura);
            totalDosItens.Text = itens.Count == 0 ? "Sem itens: informe o valor total diretamente." : $"{itens.Count} item(ns) · Total: {Dinheiro.Formatar(itens.Sum(i => i.Total))}";
            previa.Text = "";
        }
        // O item só entra na lista após validação de quantidade, preço e total acumulado.
        var adicionarItem = Botao("+ Adicionar item", () =>
        {
            try
            {
                RegrasDoOrcamento.Texto(nomeDoItem.Text?.Trim(), "Nome do item", 100);
                if (!System.Text.RegularExpressions.Regex.IsMatch(quantidadeDoItem.Text ?? "", @"^\d{1,6}(,\d{1,3})?$") || !decimal.TryParse(quantidadeDoItem.Text, NumberStyles.AllowDecimalPoint, Dinheiro.Cultura, out var unidades) || unidades <= 0 || unidades > 100_000) throw new ErroDeOrcamento("Quantidade: use um número entre 0,001 e 100.000, com vírgula para frações.");
                if (itens.Count >= 500) throw new ErroDeOrcamento("A lista permite até 500 itens.");
                var item = new ItemDaDespesa { Nome = nomeDoItem.Text!.Trim(), Quantidade = unidades, PrecoUnitario = Dinheiro.Interpretar(preco.Text, "Preço", true) };
                Dinheiro.Validar(item.Total, "Total do item", true); Dinheiro.Validar(itens.Sum(i => i.Total) + item.Total, "Total da lista", true);
                itens.Add(item); nomeDoItem.Text = ""; quantidadeDoItem.Text = "1"; preco.Text = ""; erro.Text = ""; AtualizarItens();
            }
            catch (Exception ex) { erro.Text = ErroAmigavel(ex); erro.BringIntoView(); }
        });
        var painelDeItens = Pilha(Texto("Opcional: detalhe cada item da compra", "muted"), listaDeItens,
            Campo("Produto ou serviço", nomeDoItem), Colunas(Campo("Quantidade", quantidadeDoItem), Campo("Preço unitário (R$)", preco)), adicionarItem, totalDosItens);
        var expansor = new Expander { Header = "Lista de itens da compra", Content = painelDeItens, IsExpanded = itens.Count > 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (existente?.TotalDeParcelas > 1) expansor.IsEnabled = false;
        // "Repetir todo mês" só existe no cadastro: cria um modelo e os lançamentos dos próximos meses. Não combina com parcelas nem itens.
        var repetir = new CheckBox { Content = "Repetir todo mês", IsChecked = false }; AutomationProperties.SetName(repetir, "Repetir todo mês");
        repetir.IsCheckedChanged += (_, _) =>
        {
            var ligado = repetir.IsChecked == true; if (ligado) parcelas.Text = "1";
            parcelas.IsEnabled = !ligado; expansor.IsEnabled = !ligado;
        };
        var painelDeRepeticao = existente is null ? Pilha(repetir, Texto($"Para aluguel, internet, assinaturas e contas fixas. Cria a despesa em cada mês, a partir do vencimento, até {MotorDeRecorrencias.MesesDeHorizonte} meses à frente. Para parar depois, use Recorrentes na lista de despesas.", "muted")) : Pilha();
        painelDeRepeticao.Spacing = 6;
        AtualizarItens();
        // Converte os campos em lançamentos candidatos, valida o conjunto completo e não grava nada em disco.
        (List<Despesa> Lancamentos, Recorrencia? Recorrencia, DadosDoOrcamento Candidato) MontarLancamentos()
        {
            // Evita perder um item digitado que ainda não foi confirmado pelo botão Adicionar item.
            if (!string.IsNullOrWhiteSpace(nomeDoItem.Text) || !string.IsNullOrWhiteSpace(preco.Text)) throw new ErroDeOrcamento("Há um item não adicionado. Clique em “Adicionar item” ou limpe seus campos antes de salvar.");
            var vencimento = InterpretarData(data.Text, "Vencimento");
            // A data do pagamento é opcional, mas, se preenchida, precisa ser uma data válida.
            DateOnly? pagaEm = paga.IsChecked == true && !string.IsNullOrWhiteSpace(dataDoPagamento.Text) ? InterpretarData(dataDoPagamento.Text, "Data do pagamento") : null;
            if (!int.TryParse(parcelas.Text, out var quantidade) || quantidade is < 1 or > 120) throw new ErroDeOrcamento("Use de 1 a 120 parcelas.");
            if (quantidade > 1 && paga.IsChecked == true && existente is null) throw new ErroDeOrcamento("Crie as parcelas em aberto e marque cada parcela paga na lista de despesas.");
            var repete = repetir.IsChecked == true && existente is null;
            if (repete && (quantidade > 1 || itens.Count > 0)) throw new ErroDeOrcamento("Despesas que se repetem todo mês não usam parcelas nem lista de itens.");
            if (repete && paga.IsChecked == true) throw new ErroDeOrcamento("Crie a despesa recorrente em aberto e marque cada mês como pago na lista de despesas.");
            var lancamento = new Despesa
            {
                Id = existente?.Id ?? Guid.NewGuid(), Titulo = titulo.Text?.Trim() ?? "", Categoria = categorias[categoria.SelectedIndex], Vencimento = vencimento,
                Valor = itens.Count > 0 ? itens.Sum(i => i.Total) : Dinheiro.Interpretar(valor.Text, "Valor", true), Paga = paga.IsChecked == true, PagaPor = paga.IsChecked == true && pagador.SelectedIndex > 0 ? dados.Pessoas[pagador.SelectedIndex - 1].Id : null, DataDoPagamento = pagaEm, IdDeOrigem = existente?.IdDeOrigem,
                Divisao = (ModoDeDivisao)divisao.SelectedIndex, Participantes = participantes.Where(p => p.Verificar.IsChecked == true).Select(p => p.Pessoa.Id).ToList(), Itens = [..itens],
                GrupoDeParcelas = existente?.GrupoDeParcelas, TotalDeParcelas = existente?.TotalDeParcelas ?? 1, NumeroDaParcela = existente?.NumeroDaParcela ?? 1,
                // Editar um mês de uma recorrência preserva o vínculo (e o mês de origem) para ela não recriá-lo.
                IdDaRecorrencia = existente?.IdDaRecorrencia, MesDaRecorrencia = existente?.MesDaRecorrencia
            };
            if (repete)
            {
                var recorrencia = new Recorrencia { Titulo = lancamento.Titulo, Categoria = lancamento.Categoria, Valor = lancamento.Valor, DiaDoMes = vencimento.Day, Divisao = lancamento.Divisao, Participantes = [.. lancamento.Participantes], MesInicial = Datas.Mes(vencimento) };
                var plano = ArmazenamentoLocal.Clonar(dados); plano.Recorrencias.Add(recorrencia); MotorDeRecorrencias.Gerar(plano, DateOnly.FromDateTime(DateTime.Today)); RegrasDoOrcamento.Validar(plano);
                return (plano.Despesas.Where(e => e.IdDaRecorrencia == recorrencia.Id).OrderBy(e => e.Vencimento).ToList(), recorrencia, plano);
            }
            // Cadastro pode expandir uma compra em parcelas; edição preserva a identidade de um lançamento existente.
            var lancamentos = existente is null ? RegrasDoOrcamento.Parcelar(lancamento, quantidade) : [lancamento];
            var candidato = ArmazenamentoLocal.Clonar(dados); if (existente is not null) candidato.Despesas.RemoveAll(e => e.Id == existente.Id); candidato.Despesas.AddRange(lancamentos); RegrasDoOrcamento.Validar(candidato);
            return (lancamentos, null, candidato);
        }
        var painel = Pilha(Cabecalho(existente is null ? "Nova despesa" : "Editar despesa", existente?.TotalDeParcelas > 1 ? $"Alteração apenas da parcela {existente.NumeroDaParcela}/{existente.TotalDeParcelas}." : existente?.IdDaRecorrencia is not null ? "Esta despesa se repete todo mês. A alteração vale só para este mês." : "Preencha os dados e escolha como dividir."),
            Campo("Descrição", titulo), Colunas(Campo("Categoria", categoria), Campo("Vencimento (dd/mm/aaaa)", data)),
            Colunas(Campo(existente?.TotalDeParcelas > 1 ? "Valor desta parcela (R$)" : "Valor total da compra / dívida (R$)", valor), Campo("Número de parcelas", parcelas)), painelDeRepeticao,
            Texto("Em dívidas, inclua os juros negociados no valor total. Listas de itens são lançadas à vista.", "muted"), expansor,
            Campo("Como esta despesa será paga?", divisao), Texto("Selecione os responsáveis (apenas um para despesas individuais):", "muted"), painelDePessoas, paga, campoDoPagador,
            Texto("A divisão considera a renda do mês do vencimento. Editar uma despesa individual também recalcula as despesas proporcionais desse mês.", "muted"),
            Botao("Conferir divisão antes de salvar", () =>
            {
                try
                {
                    // A prévia usa uma cópia já contendo a edição, pois despesas individuais alteram a base do rateio.
                    var (lancamentos, recorrencia, candidato) = MontarLancamentos();
                    var rateio = MotorFinanceiro.Ratear(candidato, lancamentos[0]);
                    var tituloDaSecao = recorrencia is not null ? $"Repete todo mês no dia {recorrencia.DiaDoMes}: {lancamentos.Count} lançamentos, até {lancamentos[^1].MesDaRecorrencia:MM/yyyy}. Valor mensal: {Dinheiro.Formatar(lancamentos[0].Valor)}\n" : lancamentos.Count > 1 ? $"Primeira de {lancamentos.Count} parcelas: {Dinheiro.Formatar(lancamentos[0].Valor)}\n" : "";
                    previa.Text = tituloDaSecao + string.Join("\n", rateio.Valores.Select(a => $"{dados.Pessoas.First(p => p.Id == a.Key).Nome}: {Dinheiro.Formatar(a.Value)}")) + (rateio.Aviso is null ? "" : "\n" + rateio.Aviso); erro.Text = "";
                }
                catch (Exception ex) { erro.Text = ErroAmigavel(ex); previa.Text = ""; erro.BringIntoView(); }
            }), previa, erro);
        var dialogo = Dialogo("Despesa", painel, 760); dialogo.Height = 800;
        painel.Children.Add(Linha(Botao("Cancelar", () => dialogo.Close()), Botao("Salvar despesa", () =>
        {
            try
            {
                // Repete a validação no salvamento: a prévia não substitui a conferência dos campos atuais.
                var (lancamentos, recorrencia, _) = MontarLancamentos();
                Gravar(d =>
                {
                    // Recorrência: guarda o modelo e deixa o motor criar os meses; caso contrário, grava os lançamentos montados.
                    if (recorrencia is not null) { d.Recorrencias.Add(recorrencia); MotorDeRecorrencias.Gerar(d, DateOnly.FromDateTime(DateTime.Today)); return; }
                    if (existente is not null) d.Despesas.RemoveAll(e => e.Id == existente.Id); d.Despesas.AddRange(lancamentos);
                }); dialogo.Close();
            }
            catch (Exception ex) { erro.Text = ErroAmigavel(ex); erro.BringIntoView(); }
        }, "primary")));
        await dialogo.ShowDialog(this);
    }
}
