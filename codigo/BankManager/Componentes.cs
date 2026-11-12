using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using BankManager.Nucleo;

namespace BankManager;
/// <summary>
/// Fábricas de controles compartilhados. As classes visuais referenciam os estilos em Aplicativo.axaml; não contêm regras financeiras.
/// </summary>
internal static class Componentes
{
    private static WindowIcon? iconeDoAplicativo;
    /// <summary>
    /// Ícone do aplicativo (janela e diálogos), carregado uma vez a partir dos recursos embutidos.
    /// </summary>
    public static WindowIcon IconeDoAplicativo => iconeDoAplicativo ??= new WindowIcon(AssetLoader.Open(new Uri("avares://BankManager/Recursos/icone.png")));
    /// <summary>
    /// Cria um ícone vetorial pelo nome definido em Estilos/Icones.axaml (por exemplo "Home" usa Icon.Home).
    /// Os traços são desenhados em uma área 24x24 e ampliados ou reduzidos para o tamanho pedido; a cor vem do estilo Path.icon.
    /// </summary>
    public static Control Icone(string nome, double tamanho = 18)
    {
        // A geometria não muda com o tema, então basta buscá-la uma vez; só a cor do traço é dinâmica (no estilo).
        if (Application.Current is null || !Application.Current.TryFindResource("Icon." + nome, out var geometria) || geometria is not Geometry dados) throw new ArgumentException("Ícone desconhecido: " + nome);
        var caminho = new Avalonia.Controls.Shapes.Path { Data = dados }; caminho.Classes.Add("icon");
        return new Viewbox { Width = tamanho, Height = tamanho, IsHitTestVisible = false, Child = new Canvas { Width = 24, Height = 24, Children = { caminho } } };
    }
    /// <summary>
    /// Cria texto com quebra automática e, opcionalmente, uma classe de estilo.
    /// </summary>
    public static TextBlock Texto(string texto, string? estilo = null)
    {
        var bloco = new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap };
        if (estilo is not null) bloco.Classes.Add(estilo);
        return bloco;
    }
    /// <summary>
    /// Organiza os controles verticalmente, usando o espaçamento padrão do aplicativo.
    /// </summary>
    public static StackPanel Pilha(params Control[] filhos)
    {
        var painel = new StackPanel { Spacing = 14 };
        foreach (var filho in filhos) painel.Children.Add(filho);
        return painel;
    }
    /// <summary>
    /// Reutiliza a pilha padrão em orientação horizontal, preservando a ordem dos controles.
    /// </summary>
    public static StackPanel Linha(params Control[] filhos)
    {
        var painel = Pilha(filhos); painel.Orientation = Orientation.Horizontal; painel.Spacing = 10; return painel;
    }
    /// <summary>
    /// Envolve um conteúdo em um cartão. Estilos: "card" (translúcido, o padrão), "solid" (branco, usado nas metas)
    /// e "summary" (escuro, do resumo da casa).
    /// </summary>
    public static Border Cartao(Control conteudo, string estilo = "card")
    {
        var borda = new Border { Child = conteudo }; borda.Classes.Add(estilo); return borda;
    }
    /// <summary>
    /// Cria um botão e liga seu clique à ação recebida. Ações assíncronas da janela usam o tratamento de erros de Run.
    /// </summary>
    /// <param name="soIcone">Mostra só o ícone (por exemplo as setas do mês); o rótulo continua como dica e nome de automação.</param>
    public static Button Botao(string texto, Action acao, string? estilo = null, string? icone = null, bool soIcone = false)
    {
        var botao = new Button { Content = texto };
        // Botão só com ícone: quadrado de 40 px; o texto vira dica (tooltip) e nome para leitores de tela.
        if (icone is not null && soIcone) { botao.Content = Icone(icone); botao.MinWidth = 40; botao.Padding = new Thickness(0); ToolTip.SetTip(botao, texto); }
        // Com ícone, o conteúdo vira ícone + rótulo; o nome de automação mantém o rótulo para leitores de tela e testes.
        else if (icone is not null) { var fileira = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; fileira.Children.Add(Icone(icone)); fileira.Children.Add(new TextBlock { Text = texto, VerticalAlignment = VerticalAlignment.Center }); botao.Content = fileira; }
        AutomationProperties.SetName(botao, texto);
        // Aceita várias classes separadas por espaço (por exemplo "ghost danger").
        if (estilo is not null) foreach (var nome in estilo.Split(' ', StringSplitOptions.RemoveEmptyEntries)) botao.Classes.Add(nome);
        botao.Click += (_, _) => acao(); return botao;
    }
    /// <summary>
    /// Cria um campo com texto inicial, indicação de preenchimento e limite visual de caracteres; o domínio valida o conteúdo.
    /// </summary>
    public static TextBox CampoDeTexto(string? texto = null, string? exemplo = null, int maximo = 120) => new() { Text = texto ?? "", PlaceholderText = exemplo, MaxLength = maximo };
    /// <summary>
    /// Associa rótulo e ajuda ao controle, incluindo metadados de acessibilidade para tecnologias assistivas.
    /// </summary>
    public static Control Campo(string titulo, Control entrada, string? dica = null)
    {
        AutomationProperties.SetName(entrada, titulo);
        if (dica is not null) AutomationProperties.SetHelpText(entrada, dica);
        return dica is null ? Pilha(Texto(titulo), entrada) : Pilha(Texto(titulo), entrada, Texto(dica, "muted"));
    }
    /// <summary>
    /// Distribui controles em colunas de mesma largura, aplicando espaço apenas entre as colunas.
    /// </summary>
    public static Grid Colunas(params Control[] filhos)
    {
        var grade = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", filhos.Select(_ => "*"))) };
        for (int i = 0; i < filhos.Length; i++)
        {
            filhos[i].Margin = new Thickness(i == 0 ? 0 : 8, 0, i == filhos.Length - 1 ? 0 : 8, 0);
            Grid.SetColumn(filhos[i], i); grade.Children.Add(filhos[i]);
        }
        return grade;
    }
    /// <summary>
    /// Cria uma seleção por índice; o chamador mantém a correspondência entre índices e opções do domínio.
    /// </summary>
    public static ComboBox ListaDeOpcoes(IEnumerable<string> opcoes, int indice = 0) => new() { ItemsSource = opcoes.ToArray(), SelectedIndex = indice };
    /// <summary>
    /// Cria uma janela centralizada no proprietário e com rolagem para formulários maiores que a área disponível.
    /// </summary>
    public static Window Dialogo(string titulo, Control conteudo, double largura = 620) => new()
    {
        Title = titulo, Width = largura, Height = 720, MinWidth = 480, MinHeight = 400, Icon = IconeDoAplicativo,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        // A classe "dialog" troca o degradê da janela pela superfície sólida.
        Classes = { "dialog" },
        Content = new ScrollViewer { Content = new Border { Padding = new Thickness(28), Child = conteudo } }
    };
    /// <summary>
    /// Abre uma confirmação modal. Somente o botão Confirmar devolve verdadeiro; cancelar ou fechar não autoriza a ação.
    /// </summary>
    public static async Task<bool> Confirmar(Window dono, string titulo, string mensagem)
    {
        var painel = Pilha(Texto(titulo, "section"), Texto(mensagem, "muted"));
        var dialogo = Dialogo(titulo, painel); dialogo.Height = 320;
        painel.Children.Add(Linha(Botao("Cancelar", () => dialogo.Close(false)), Botao("Confirmar", () => dialogo.Close(true), "primary")));
        return await dialogo.ShowDialog<bool>(dono);
    }
    /// <summary>
    /// Pede um texto curto em um diálogo. Devolve o texto digitado ou nulo se o usuário cancelar; a validação fica com quem chama.
    /// </summary>
    public static async Task<string?> Perguntar(Window dono, string titulo, string rotulo, string inicial, int maximo)
    {
        var entrada = CampoDeTexto(inicial, null, maximo); string? resultado = null;
        var painel = Pilha(Texto(titulo, "section"), Campo(rotulo, entrada));
        var dialogo = Dialogo(titulo, painel, 480); dialogo.Height = 300;
        painel.Children.Add(Linha(Botao("Cancelar", () => dialogo.Close()), Botao("Salvar", () => { resultado = entrada.Text; dialogo.Close(); }, "primary")));
        await dialogo.ShowDialog(dono); return resultado;
    }
    /// <summary>
    /// Linha divisória fina entre itens de uma lista.
    /// </summary>
    public static Border Divisoria() { var linha = new Border(); linha.Classes.Add("divider"); return linha; }
    /// <summary>
    /// Etiqueta curta de situação. Tons: "warn", "positive", "negative"; sem tom, o chip é neutro.
    /// </summary>
    public static Border Etiqueta(string texto, string? tom = null)
    {
        var etiqueta = new Border { Child = new TextBlock { Text = texto } }; etiqueta.Classes.Add("chip"); if (tom is not null) etiqueta.Classes.Add(tom); return etiqueta;
    }
    /// <summary>
    /// Círculo com a inicial do nome, usado para identificar pessoas. O forte (preto) destaca uma pessoa ao lado de outra.
    /// </summary>
    public static Border Avatar(string nome, bool forte = false)
    {
        var inicial = nome.Trim().Length > 0 ? nome.Trim()[..1].ToUpper(Dinheiro.Cultura) : "?";
        var avatar = new Border { Child = new TextBlock { Text = inicial }, VerticalAlignment = VerticalAlignment.Center }; avatar.Classes.Add("avatar"); avatar.Classes.Set("strong", forte); return avatar;
    }
    /// <summary>
    /// Nome do ícone de uma categoria. As oito categorias padrão têm ícone próprio; as criadas pelo usuário usam a etiqueta.
    /// </summary>
    public static string NomeDoIconeDaCategoria(string categoria) => categoria switch
    {
        "Moradia" => "Home", "Mercado" => "Cart", "Transporte" => "Car", "Saúde" => "Heart", "Educação" => "Book", "Lazer" => "Smile", "Serviços" => "Zap", _ => "Tag"
    };
    /// <summary>
    /// Círculo de 36 px com um ícone. Com "income", fica verde (entrada de dinheiro).
    /// </summary>
    public static Border CirculoComIcone(string icone, bool renda = false)
    {
        var circulo = new Border { Child = Icone(icone, 17) }; circulo.Classes.Add("iconcircle"); circulo.Classes.Set("income", renda); return circulo;
    }
    /// <summary>
    /// Controle de opções lado a lado (apenas uma ativa). Atualiza o destaque sozinho e avisa a opção escolhida.
    /// </summary>
    public static Border Segmentado(string[] opcoes, int selecionada, Action<int> mudou)
    {
        var botoes = new List<Button>(); var fileira = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        for (var i = 0; i < opcoes.Length; i++)
        {
            var indice = i; var botao = Botao(opcoes[i], () => { for (var j = 0; j < botoes.Count; j++) botoes[j].Classes.Set("active", j == indice); mudou(indice); }, "seg");
            botao.Classes.Set("active", i == selecionada); botoes.Add(botao); fileira.Children.Add(botao);
        }
        var caixa = new Border { Child = fileira, VerticalAlignment = VerticalAlignment.Center }; caixa.Classes.Add("segmented"); return caixa;
    }
    /// <summary>
    /// Seção de ajuda que abre e fecha ao clicar no título, no lugar de um texto longo sempre visível.
    /// </summary>
    public static Control Recolhivel(string titulo, string corpo)
    {
        var conteudo = Texto(corpo, "muted"); conteudo.IsVisible = false; conteudo.Margin = new Thickness(12, 0, 12, 12);
        var setinha = Icone("ChevronDown", 16); setinha.RenderTransform = new RotateTransform(-90);
        var tituloDaSecao = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var rotulo = new TextBlock { Text = titulo, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center }; setinha.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(setinha, 1);
        tituloDaSecao.Children.Add(rotulo); tituloDaSecao.Children.Add(setinha);
        var cabecalho = new Button { Content = tituloDaSecao, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch }; cabecalho.Classes.Add("ghost");
        AutomationProperties.SetName(cabecalho, titulo);
        cabecalho.Click += (_, _) => { conteudo.IsVisible = !conteudo.IsVisible; setinha.RenderTransform = new RotateTransform(conteudo.IsVisible ? 0 : -90); };
        var painel = new StackPanel { Spacing = 0 }; painel.Children.Add(cabecalho); painel.Children.Add(conteudo); return painel;
    }
    /// <summary>
    /// Acrescenta uma classe de estilo e devolve o próprio controle, para compor miniaturas em uma só expressão.
    /// </summary>
    public static T ComClasse<T>(this T controle, string estilo) where T : StyledElement { controle.Classes.Add(estilo); return controle; }
}
