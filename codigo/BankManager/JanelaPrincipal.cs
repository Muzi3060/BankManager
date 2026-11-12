using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BankManager.Nucleo;
using static BankManager.Componentes;

namespace BankManager;
/// <summary>
/// Janela principal: guarda o estado ativo, a navegação e o fluxo de gravação. Cada página é uma parte desta
/// classe em seu próprio arquivo, na pasta Pages (Dashboard, People, Expenses, Forecast e Settings).
/// Os cálculos e a persistência ficam no Core.
/// </summary>
public sealed partial class JanelaPrincipal : Window
{
    // Estado ativo: só é trocado após persistência bem-sucedida. Não editar diretamente nos formulários.
    private DadosDoOrcamento dados = new();
    private ArmazenamentoLocal? armazenamento;
    // Preferências (tema) ficam em arquivo próprio, na mesma pasta de dados, e não dependem de o armazenamento abrir.
    private Preferencias preferencias = new();
    private readonly string pastaDasPreferencias;
    // Distingue orçamento vazio válido de falha de leitura; evita salvar por cima de uma base não carregada.
    private bool pronto;
    private DateOnly mes = Datas.Mes(DateOnly.FromDateTime(DateTime.Today));
    // A estrutura fixa permanece; Render substitui apenas o conteúdo da página.
    private readonly ContentControl pagina = new();
    // Aviso global no topo do conteúdo: erros (tom de alerta) e confirmações (tom positivo).
    private readonly TextBlock aviso = Texto("", "alerta");
    private readonly Border caixaDoAviso = new();
    private readonly List<Button> navegacao = [];
    // Índices de página: 0 resumo, 1 pessoas, 2 despesas, 3 previsões e metas, 4 ajustes.
    private int paginaAtual;
    // Filtros são mantidos na janela para sobreviver à reconstrução da página após um salvamento.
    private string filtro = "";
    private int filtroDeSituacao;
    private int filtroDeCategoria;
    // Na página de despesas: falso mostra o mês selecionado, verdadeiro busca em todos os meses.
    private bool todosOsMeses;
    // Modo de seleção da página Despesas: a lista ganha caixas de marcação e uma barra flutuante para compartilhar as marcadas.
    private bool selecionando;
    private readonly HashSet<Guid> idsSelecionados = [];
    private readonly Border barraDeSelecao = new() { IsVisible = false };
    private readonly TextBlock textoDaQuantidade = new() { FontWeight = FontWeight.SemiBold };
    private readonly TextBlock textoDoTotal = new();
    private Button? botaoCompartilhar;
    /// <summary>
    /// Só para os testes de interface, que não conseguem operar o seletor de arquivos do sistema: quando preenchido, "Salvar arquivo"
    /// grava direto neste caminho, sem abrir o seletor.
    /// </summary>
    public string? CaminhoDeTesteParaSalvar { get; set; }
    /// <summary>
    /// Só para os testes de interface: quando preenchido, "Importar despesas compartilhadas" e "Trocar arquivo" abrem este caminho.
    /// </summary>
    public string? CaminhoDeTesteParaAbrir { get; set; }
    // Aba aberta em "Previsões e metas": 0 metas, 1 previsão, 2 recorrentes, 3 acerto de contas.
    private int abaDePrevisoes;
    /// <summary>
    /// Monta a estrutura fixa da janela, aplica o tema salvo, tenta carregar os dados e exibe recuperação se a abertura falhar.
    /// </summary>
    public JanelaPrincipal()
    {
        Title = "BankManager"; Width = 1220; Height = 840; MinWidth = 960; MinHeight = 640; Icon = IconeDoAplicativo;
        // A pasta de dados alternativa permite executar testes sem tocar nos dados reais da conta.
        var pastaDeDados = Environment.GetEnvironmentVariable("BANKMANAGER_DATA_DIR");
        pastaDasPreferencias = ArmazenamentoLocal.PastaPadrao(pastaDeDados);
        // O tema é aplicado antes de a janela aparecer, para não piscar no tema errado.
        preferencias = ArmazenamentoDePreferencias.Carregar(pastaDasPreferencias); Aplicativo.AplicarTema(preferencias.Tema);
        // Menu lateral de 188 px, direto sobre o degradê da janela: marca em texto, quatro destinos e "Ajustes" no rodapé.
        var menuLateral = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(12, 0, 8, 0) };
        menuLateral.Children.Add(new Border { Padding = new Thickness(12, 30, 0, 22), Child = Texto("BankManager", "marca") });
        var menu = new StackPanel { Spacing = 4 };
        (string Rotulo, string Icone)[] itens = [("Visão geral", "Casa"), ("Pessoas e rendas", "Pessoas"), ("Despesas", "Lista"), ("Previsões e metas", "Tendencia"), ("Ajustes", "Ajustes")];
        for (var i = 0; i < itens.Length; i++)
        {
            // Copia o índice para que cada callback navegue à página correspondente, em vez do valor final do laço.
            var indice = i; var botao = Botao(itens[i].Rotulo, () => IrPara(indice), "menu", itens[i].Icone); navegacao.Add(botao);
            // "Ajustes" (o último) vai para o rodapé do menu, em tom secundário.
            if (i < itens.Length - 1) menu.Children.Add(botao);
            else { botao.Classes.Add("rodape"); botao.Margin = new Thickness(0, 0, 0, 18); Grid.SetRow(botao, 2); menuLateral.Children.Add(botao); }
        }
        Grid.SetRow(menu, 1); menuLateral.Children.Add(menu);
        // Conteúdo: aviso global (quando há) e a página rolável.
        caixaDoAviso.Classes.Add("aviso"); caixaDoAviso.Child = aviso; caixaDoAviso.IsVisible = false; caixaDoAviso.Margin = new Thickness(24, 20, 32, 0);
        var corpo = new Grid { RowDefinitions = new RowDefinitions("Auto,*") }; corpo.Children.Add(caixaDoAviso);
        var rolagem = new ScrollViewer { Content = new Border { Padding = new Thickness(24, 26, 32, 32), Child = pagina }, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(rolagem, 1); corpo.Children.Add(rolagem);
        // Barra flutuante do modo de seleção: fica sobre a página (fora da área rolável), então continua visível com a lista rolando.
        textoDoTotal.Classes.Add("secundario"); textoDoTotal.Margin = new Thickness(0, 0, 10, 0);
        botaoCompartilhar = Botao("Compartilhar…", AbrirExportacao, "barra-acao", "Compartilhar");
        var cancelarSelecao = Botao("Cancelar", EncerrarSelecao, "barra-fantasma"); AutomationProperties.SetName(cancelarSelecao, "Cancelar seleção");
        barraDeSelecao.Child = Linha(textoDaQuantidade, textoDoTotal, cancelarSelecao, botaoCompartilhar); ((StackPanel)barraDeSelecao.Child).Spacing = 14;
        barraDeSelecao.Classes.Add("barra-selecao"); barraDeSelecao.Margin = new Thickness(0, 0, 0, 22); Grid.SetRow(barraDeSelecao, 1); corpo.Children.Add(barraDeSelecao);
        var raiz = new Grid { ColumnDefinitions = new ColumnDefinitions("188,*") }; raiz.Children.Add(menuLateral); Grid.SetColumn(corpo, 1); raiz.Children.Add(corpo);
        // A camada dos diálogos sobrepostos cobre a janela inteira, por cima do menu e da página.
        camadaDaSobreposicao.SizeChanged += (_, _) => AjustarSobreposicao();
        Content = new Panel { Children = { raiz, camadaDaSobreposicao } };
        // Atalho Ctrl+N: nova despesa de qualquer tela (diálogos modais bloqueiam a janela, então não abre dois).
        // Com um diálogo sobreposto aberto, o atalho não age e a tecla Esc pertence ao diálogo; sem diálogo, Esc sai do modo de seleção.
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && SobreposicaoAberta) { e.Handled = true; escDaSobreposicao?.Invoke(); }
            else if (e.Key == Key.Escape && selecionando) { e.Handled = true; EncerrarSelecao(); }
            else if (e.Key == Key.N && e.KeyModifiers == KeyModifiers.Control && pronto && !SobreposicaoAberta) { e.Handled = true; _ = Executar(() => EditarDespesa()); }
        };
        try { armazenamento = new ArmazenamentoLocal(pastaDeDados); dados = armazenamento.Carregar(); pronto = true; CompletarRecorrencias(); }
        catch (Exception ex) { MostrarErro(ex); }
        // Libera o bloqueio de armazenamento mesmo quando a base falhou ao carregar.
        Closed += (_, _) => armazenamento?.Dispose();
        Desenhar();
    }
    /// <summary>
    /// Cria os lançamentos de despesas recorrentes que faltam (a janela avança com o tempo). Se salvar falhar, os dados já carregados
    /// continuam válidos e o usuário é avisado.
    /// </summary>
    private void CompletarRecorrencias()
    {
        try
        {
            var proximo = ArmazenamentoLocal.Clonar(dados);
            if (armazenamento is null || MotorDeRecorrencias.Gerar(proximo, DateOnly.FromDateTime(DateTime.Today)) == 0) return;
            armazenamento.Salvar(proximo); dados = proximo;
        }
        catch (Exception ex) { MostrarErro(ex); }
    }
    /// <summary>
    /// Troca de página e reconstrói a tela.
    /// </summary>
    private void IrPara(int indice) { paginaAtual = indice; if (indice != 2) { todosOsMeses = false; selecionando = false; idsSelecionados.Clear(); } Desenhar(); }
    /// <summary>
    /// Cabeçalho comum das páginas: título (e uma linha de apoio) à esquerda; à direita, a navegação de mês em uma pílula
    /// translúcida (‹ mês ›) e o botão primário "Nova despesa". Clicar no nome do mês volta ao mês atual.
    /// </summary>
    private Control CabecalhoDaPagina(string titulo, string? subtitulo, bool navegacaoDeMes = true)
    {
        var titulos = subtitulo is null ? Pilha(Texto(titulo, "titulo")) : Pilha(Texto(titulo, "titulo"), Texto(subtitulo, "apagado")); titulos.Spacing = 2; titulos.VerticalAlignment = VerticalAlignment.Center;
        var acoes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        if (navegacaoDeMes)
        {
            var rotulo = Botao(mes.ToString("MMMM 'de' yyyy", Dinheiro.Cultura), () => { mes = Datas.Mes(DateOnly.FromDateTime(DateTime.Today)); Desenhar(); }, "mes");
            rotulo.MinWidth = 150; rotulo.HorizontalContentAlignment = HorizontalAlignment.Center; rotulo.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(rotulo, "Voltar ao mês atual"); ToolTip.SetTip(rotulo, "Voltar ao mês atual");
            var pilula = new Border { Child = Linha(Botao("Mês anterior", () => MudarMes(-1), "fantasma redondo", "SetaEsquerda", soIcone: true), rotulo, Botao("Próximo mês", () => MudarMes(1), "fantasma redondo", "SetaDireita", soIcone: true)) };
            ((StackPanel)pilula.Child).Spacing = 0; pilula.Classes.Add("pilula"); acoes.Children.Add(pilula);
        }
        acoes.Children.Add(Botao("Nova despesa", () => _ = Executar(() => EditarDespesa()), "primario", "Mais"));
        var cabecalho = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 22) }; Grid.SetColumn(acoes, 1); cabecalho.Children.Add(titulos); cabecalho.Children.Add(acoes);
        return cabecalho;
    }
    /// <summary>
    /// Mostra um aviso no topo do conteúdo: alerta por padrão, ou confirmação em tom positivo.
    /// </summary>
    private void Avisar(string texto, bool sucesso = false)
    {
        aviso.Text = texto; aviso.Classes.Set("alerta", !sucesso); aviso.Classes.Set("positivo", sucesso); caixaDoAviso.Classes.Set("positivo", sucesso); caixaDoAviso.IsVisible = true;
    }
    /// <summary>
    /// Esconde o aviso global.
    /// </summary>
    private void EsconderAviso() => caixaDoAviso.IsVisible = false;
    /// <summary>
    /// Navega entre competências e limita o ano a 2099 para que a previsão do mês seguinte permaneça no domínio suportado.
    /// </summary>
    private void MudarMes(int direcao)
    {
        var proximo = mes.AddMonths(direcao);
        if (proximo.Year is >= 2000 and <= 2099) { mes = proximo; Desenhar(); }
    }
    /// <summary>
    /// Traduz exceções para o aviso global, exibindo detalhes de negócio e mensagens genéricas para erros técnicos.
    /// </summary>
    private void MostrarErro(Exception ex)
    {
        Avisar(ex switch
        {
            ErroDeOrcamento => ex.Message,
            UnauthorizedAccessException => "Sem permissão para acessar o arquivo. Escolha uma pasta gravável. Seus dados não foram substituídos.",
            IOException => "Não foi possível ler ou salvar os dados. Verifique espaço, permissões e se o arquivo está em uso. A operação não foi concluída.",
            _ => "Não foi possível concluir a operação. Seus dados atuais foram preservados."
        });
    }
    /// <summary>
    /// Executa ações assíncronas disparadas pela interface e encaminha falhas para o aviso global.
    /// </summary>
    private async Task Executar(Func<Task> acao)
    {
        try { EsconderAviso(); await acao(); }
        catch (Exception ex) { MostrarErro(ex); }
    }
    /// <summary>
    /// Aplica uma alteração a uma cópia profunda, salva e só então adota o novo estado e reconstrói a página.
    /// </summary>
    private void Gravar(Action<DadosDoOrcamento> mudanca)
    {
        if (armazenamento is null || !pronto) throw new ErroDeOrcamento("Os dados ainda não estão disponíveis.");
        // Se a alteração ou Save lançar uma exceção, a atribuição de data não ocorre e o estado ativo permanece.
        var proximo = ArmazenamentoLocal.Clonar(dados); mudanca(proximo); armazenamento.Salvar(proximo); dados = proximo; Desenhar();
    }
    /// <summary>
    /// Atualiza mês e navegação e reconstrói a página selecionada; enquanto os dados não estão prontos, oferece recuperação.
    /// </summary>
    private void Desenhar()
    {
        for (var i = 0; i < navegacao.Count; i++) navegacao[i].Classes.Set("ativo", i == paginaAtual);
        AtualizarBarraDeSelecao();
        // Falha ao abrir não significa uma base nova: a edição comum fica bloqueada até recuperação válida.
        if (!pronto)
        {
            pagina.Content = Pilha(Texto("Seus dados precisam de atenção", "titulo"), Texto("Não vamos substituir um arquivo que não pôde ser aberto. Feche outras janelas do aplicativo ou restaure uma cópia válida.", "apagado"),
                Botao("Tentar abrir novamente", () => _ = Executar(() => { armazenamento ??= new ArmazenamentoLocal(Environment.GetEnvironmentVariable("BANKMANAGER_DATA_DIR")); dados = armazenamento.Carregar(); pronto = true; CompletarRecorrencias(); Desenhar(); return Task.CompletedTask; }), "primario"),
                Botao("Recuperar cópia automática", () => _ = Executar(Recuperar)), Botao("Importar backup", () => _ = Executar(Importar)));
            return;
        }
        pagina.Content = paginaAtual switch { 1 => PaginaDePessoas(), 2 => PaginaDeDespesas(), 3 => PaginaDePrevisoes(), 4 => PaginaDeAjustes(), _ => VisaoGeral() };
    }
    /// <summary>
    /// Compõe título e explicação curta de uma página usando os estilos compartilhados.
    /// </summary>
    private static Control Cabecalho(string titulo, string subtitulo) => Pilha(Texto(titulo, "titulo"), Texto(subtitulo, "apagado"));
    /// <summary>
    /// Produz a mensagem local dos formulários sem exibir stack traces ou detalhes internos ao usuário.
    /// </summary>
    private static string ErroAmigavel(Exception ex) => ex is ErroDeOrcamento ? ex.Message : ex is IOException or UnauthorizedAccessException ? "Não foi possível salvar. Verifique espaço em disco, permissões e acesso à pasta. Nenhuma alteração foi aplicada." : "Não foi possível concluir. Revise os campos e tente novamente.";
}
