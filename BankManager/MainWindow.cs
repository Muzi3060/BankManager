using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
/// <summary>
/// Janela principal: guarda o estado ativo, a navegação e o fluxo de gravação. Cada página é uma parte desta
/// classe em seu próprio arquivo, na pasta Pages (Dashboard, People, Expenses, Forecast e Settings).
/// Os cálculos e a persistência ficam no Core.
/// </summary>
public sealed partial class MainWindow : Window
{
    // Estado ativo: só é trocado após persistência bem-sucedida. Não editar diretamente nos formulários.
    private BudgetData data = new();
    private LocalStore? store;
    // Preferências (tema) ficam em arquivo próprio, na mesma pasta de dados, e não dependem de o armazenamento abrir.
    private AppSettings settings = new();
    private readonly string settingsDirectory;
    // Distingue orçamento vazio válido de falha de leitura; evita salvar por cima de uma base não carregada.
    private bool ready;
    private DateOnly month = Dates.Month(DateOnly.FromDateTime(DateTime.Today));
    // A estrutura fixa permanece; Render substitui apenas o conteúdo da página.
    private readonly ContentControl page = new();
    // Aviso global no topo do conteúdo: erros (tom de alerta) e confirmações (tom positivo).
    private readonly TextBlock notice = Text("", "warn");
    private readonly Border noticeBox = new();
    private readonly List<Button> navigation = [];
    // Índices de página: 0 resumo, 1 pessoas, 2 despesas, 3 previsões e metas, 4 ajustes.
    private int currentPage;
    // Filtros são mantidos na janela para sobreviver à reconstrução da página após um salvamento.
    private string filter = "";
    private int statusFilter;
    private int categoryFilter;
    // Na página de despesas: falso mostra o mês selecionado, verdadeiro busca em todos os meses.
    private bool allMonths;
    // Modo de seleção da página Despesas: a lista ganha caixas de marcação e uma barra flutuante para compartilhar as marcadas.
    private bool selecting;
    private readonly HashSet<Guid> selectedIds = [];
    private readonly Border selectionBar = new() { IsVisible = false };
    private readonly TextBlock selectionCount = new() { FontWeight = FontWeight.SemiBold };
    private readonly TextBlock selectionTotal = new();
    private Button? selectionGo;
    /// <summary>
    /// Só para os testes de interface, que não conseguem operar o seletor de arquivos do sistema: quando preenchido, "Salvar arquivo"
    /// grava direto neste caminho, sem abrir o seletor.
    /// </summary>
    public string? TestSavePath { get; set; }
    /// <summary>
    /// Só para os testes de interface: quando preenchido, "Importar despesas compartilhadas" e "Trocar arquivo" abrem este caminho.
    /// </summary>
    public string? TestOpenPath { get; set; }
    // Aba aberta em "Previsões e metas": 0 metas, 1 previsão, 2 recorrentes, 3 acerto de contas.
    private int forecastTab;
    /// <summary>
    /// Monta a estrutura fixa da janela, aplica o tema salvo, tenta carregar os dados e exibe recuperação se a abertura falhar.
    /// </summary>
    public MainWindow()
    {
        Title = "BankManager"; Width = 1220; Height = 840; MinWidth = 960; MinHeight = 640; Icon = AppIcon;
        // A pasta de dados alternativa permite executar testes sem tocar nos dados reais da conta.
        var dataDirectory = Environment.GetEnvironmentVariable("BANKMANAGER_DATA_DIR");
        settingsDirectory = LocalStore.DefaultDirectory(dataDirectory);
        // O tema é aplicado antes de a janela aparecer, para não piscar no tema errado.
        settings = SettingsStore.Load(settingsDirectory); App.ApplyTheme(settings.Theme);
        // Menu lateral de 188 px, direto sobre o degradê da janela: marca em texto, quatro destinos e "Ajustes" no rodapé.
        var sidebar = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(12, 0, 8, 0) };
        sidebar.Children.Add(new Border { Padding = new Thickness(12, 30, 0, 22), Child = Text("BankManager", "brand") });
        var menu = new StackPanel { Spacing = 4 };
        (string Label, string Icon)[] items = [("Visão geral", "Home"), ("Pessoas e rendas", "Users"), ("Despesas", "List"), ("Previsões e metas", "Trend"), ("Ajustes", "Sliders")];
        for (var i = 0; i < items.Length; i++)
        {
            // Copia o índice para que cada callback navegue à página correspondente, em vez do valor final do laço.
            var index = i; var button = Button(items[i].Label, () => GoTo(index), "nav", items[i].Icon); navigation.Add(button);
            // "Ajustes" (o último) vai para o rodapé do menu, em tom secundário.
            if (i < items.Length - 1) menu.Children.Add(button);
            else { button.Classes.Add("footer"); button.Margin = new Thickness(0, 0, 0, 18); Grid.SetRow(button, 2); sidebar.Children.Add(button); }
        }
        Grid.SetRow(menu, 1); sidebar.Children.Add(menu);
        // Conteúdo: aviso global (quando há) e a página rolável.
        noticeBox.Classes.Add("notice"); noticeBox.Child = notice; noticeBox.IsVisible = false; noticeBox.Margin = new Thickness(24, 20, 32, 0);
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*") }; body.Children.Add(noticeBox);
        var scroll = new ScrollViewer { Content = new Border { Padding = new Thickness(24, 26, 32, 32), Child = page }, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); body.Children.Add(scroll);
        // Barra flutuante do modo de seleção: fica sobre a página (fora da área rolável), então continua visível com a lista rolando.
        selectionTotal.Classes.Add("sub"); selectionTotal.Margin = new Thickness(0, 0, 10, 0);
        selectionGo = Button("Compartilhar…", OpenShareExport, "bargo", "Share");
        var cancelSelection = Button("Cancelar", StopSelecting, "barghost"); AutomationProperties.SetName(cancelSelection, "Cancelar seleção");
        selectionBar.Child = Row(selectionCount, selectionTotal, cancelSelection, selectionGo); ((StackPanel)selectionBar.Child).Spacing = 14;
        selectionBar.Classes.Add("selectbar"); selectionBar.Margin = new Thickness(0, 0, 0, 22); Grid.SetRow(selectionBar, 1); body.Children.Add(selectionBar);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("188,*") }; root.Children.Add(sidebar); Grid.SetColumn(body, 1); root.Children.Add(body);
        // A camada dos diálogos sobrepostos cobre a janela inteira, por cima do menu e da página.
        overlayHost.SizeChanged += (_, _) => FitOverlay();
        Content = new Panel { Children = { root, overlayHost } };
        // Atalho Ctrl+N: nova despesa de qualquer tela (diálogos modais bloqueiam a janela, então não abre dois).
        // Com um diálogo sobreposto aberto, o atalho não age e a tecla Esc pertence ao diálogo; sem diálogo, Esc sai do modo de seleção.
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && OverlayOpen) { e.Handled = true; overlayEscape?.Invoke(); }
            else if (e.Key == Key.Escape && selecting) { e.Handled = true; StopSelecting(); }
            else if (e.Key == Key.N && e.KeyModifiers == KeyModifiers.Control && ready && !OverlayOpen) { e.Handled = true; _ = Run(() => EditExpense()); }
        };
        try { store = new LocalStore(dataDirectory); data = store.Load(); ready = true; TopUpRecurrences(); }
        catch (Exception ex) { ShowError(ex); }
        // Libera o bloqueio de armazenamento mesmo quando a base falhou ao carregar.
        Closed += (_, _) => store?.Dispose();
        Render();
    }
    /// <summary>
    /// Cria os lançamentos de despesas recorrentes que faltam (a janela avança com o tempo). Se salvar falhar, os dados já carregados
    /// continuam válidos e o usuário é avisado.
    /// </summary>
    private void TopUpRecurrences()
    {
        try
        {
            var next = LocalStore.Clone(data);
            if (store is null || RecurrenceEngine.Generate(next, DateOnly.FromDateTime(DateTime.Today)) == 0) return;
            store.Save(next); data = next;
        }
        catch (Exception ex) { ShowError(ex); }
    }
    /// <summary>
    /// Troca de página e reconstrói a tela.
    /// </summary>
    private void GoTo(int index) { currentPage = index; if (index != 2) { allMonths = false; selecting = false; selectedIds.Clear(); } Render(); }
    /// <summary>
    /// Cabeçalho comum das páginas: título (e uma linha de apoio) à esquerda; à direita, a navegação de mês em uma pílula
    /// translúcida (‹ mês ›) e o botão primário "Nova despesa". Clicar no nome do mês volta ao mês atual.
    /// </summary>
    private Control PageHeader(string title, string? subtitle, bool monthNavigation = true)
    {
        var titles = subtitle is null ? Stack(Text(title, "title")) : Stack(Text(title, "title"), Text(subtitle, "muted")); titles.Spacing = 2; titles.VerticalAlignment = VerticalAlignment.Center;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        if (monthNavigation)
        {
            var label = Button(month.ToString("MMMM 'de' yyyy", Money.Culture), () => { month = Dates.Month(DateOnly.FromDateTime(DateTime.Today)); Render(); }, "month");
            label.MinWidth = 150; label.HorizontalContentAlignment = HorizontalAlignment.Center; label.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(label, "Voltar ao mês atual"); ToolTip.SetTip(label, "Voltar ao mês atual");
            var pill = new Border { Child = Row(Button("Mês anterior", () => ChangeMonth(-1), "ghost round", "ChevronLeft", iconOnly: true), label, Button("Próximo mês", () => ChangeMonth(1), "ghost round", "ChevronRight", iconOnly: true)) };
            ((StackPanel)pill.Child).Spacing = 0; pill.Classes.Add("pill"); actions.Children.Add(pill);
        }
        actions.Children.Add(Button("Nova despesa", () => _ = Run(() => EditExpense()), "primary", "Plus"));
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 22) }; Grid.SetColumn(actions, 1); header.Children.Add(titles); header.Children.Add(actions);
        return header;
    }
    /// <summary>
    /// Mostra um aviso no topo do conteúdo: alerta por padrão, ou confirmação em tom positivo.
    /// </summary>
    private void Notify(string text, bool success = false)
    {
        notice.Text = text; notice.Classes.Set("warn", !success); notice.Classes.Set("positive", success); noticeBox.Classes.Set("positive", success); noticeBox.IsVisible = true;
    }
    /// <summary>
    /// Esconde o aviso global.
    /// </summary>
    private void ClearNotice() => noticeBox.IsVisible = false;
    /// <summary>
    /// Navega entre competências e limita o ano a 2099 para que a previsão do mês seguinte permaneça no domínio suportado.
    /// </summary>
    private void ChangeMonth(int direction)
    {
        var next = month.AddMonths(direction);
        if (next.Year is >= 2000 and <= 2099) { month = next; Render(); }
    }
    /// <summary>
    /// Traduz exceções para o aviso global, exibindo detalhes de negócio e mensagens genéricas para erros técnicos.
    /// </summary>
    private void ShowError(Exception ex)
    {
        Notify(ex switch
        {
            BudgetException => ex.Message,
            UnauthorizedAccessException => "Sem permissão para acessar o arquivo. Escolha uma pasta gravável. Seus dados não foram substituídos.",
            IOException => "Não foi possível ler ou salvar os dados. Verifique espaço, permissões e se o arquivo está em uso. A operação não foi concluída.",
            _ => "Não foi possível concluir a operação. Seus dados atuais foram preservados."
        });
    }
    /// <summary>
    /// Executa ações assíncronas disparadas pela interface e encaminha falhas para o aviso global.
    /// </summary>
    private async Task Run(Func<Task> action)
    {
        try { ClearNotice(); await action(); }
        catch (Exception ex) { ShowError(ex); }
    }
    /// <summary>
    /// Aplica uma alteração a uma cópia profunda, salva e só então adota o novo estado e reconstrói a página.
    /// </summary>
    private void Commit(Action<BudgetData> change)
    {
        if (store is null || !ready) throw new BudgetException("Os dados ainda não estão disponíveis.");
        // Se a alteração ou Save lançar uma exceção, a atribuição de data não ocorre e o estado ativo permanece.
        var next = LocalStore.Clone(data); change(next); store.Save(next); data = next; Render();
    }
    /// <summary>
    /// Atualiza mês e navegação e reconstrói a página selecionada; enquanto os dados não estão prontos, oferece recuperação.
    /// </summary>
    private void Render()
    {
        for (var i = 0; i < navigation.Count; i++) navigation[i].Classes.Set("active", i == currentPage);
        UpdateSelectionBar();
        // Falha ao abrir não significa uma base nova: a edição comum fica bloqueada até recuperação válida.
        if (!ready)
        {
            page.Content = Stack(Text("Seus dados precisam de atenção", "title"), Text("Não vamos substituir um arquivo que não pôde ser aberto. Feche outras janelas do aplicativo ou restaure uma cópia válida.", "muted"),
                Button("Tentar abrir novamente", () => _ = Run(() => { store ??= new LocalStore(Environment.GetEnvironmentVariable("BANKMANAGER_DATA_DIR")); data = store.Load(); ready = true; TopUpRecurrences(); Render(); return Task.CompletedTask; }), "primary"),
                Button("Recuperar cópia automática", () => _ = Run(Recover)), Button("Importar backup", () => _ = Run(Import)));
            return;
        }
        page.Content = currentPage switch { 1 => PeoplePage(), 2 => ExpensesPage(), 3 => ForecastPage(), 4 => SettingsPage(), _ => Dashboard() };
    }
    /// <summary>
    /// Compõe título e explicação curta de uma página usando os estilos compartilhados.
    /// </summary>
    private static Control Heading(string title, string subtitle) => Stack(Text(title, "title"), Text(subtitle, "muted"));
    /// <summary>
    /// Produz a mensagem local dos formulários sem exibir stack traces ou detalhes internos ao usuário.
    /// </summary>
    private static string FriendlyError(Exception ex) => ex is BudgetException ? ex.Message : ex is IOException or UnauthorizedAccessException ? "Não foi possível salvar. Verifique espaço em disco, permissões e acesso à pasta. Nenhuma alteração foi aplicada." : "Não foi possível concluir. Revise os campos e tente novamente.";
}
