using Avalonia;
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
    // Índices de página: 0 resumo, 1 pessoas, 2 despesas, 3 previsões, 4 ajustes.
    private int currentPage;
    // Filtros são mantidos na janela para sobreviver à reconstrução da página após um salvamento.
    private string filter = "";
    private int statusFilter;
    private int categoryFilter;
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
        // Menu lateral de 232 px: logo, cinco destinos e o aviso de privacidade no rodapé.
        var sidebar = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") }; sidebar.Classes.Add("sidebar");
        var logo = new Border { Child = Icon("Logo", 16), VerticalAlignment = VerticalAlignment.Center }; logo.Classes.Add("logo");
        sidebar.Children.Add(new Border { Padding = new Thickness(24, 28, 24, 16), Child = Row(logo, new TextBlock { Text = "BankManager", FontWeight = FontWeight.SemiBold, FontSize = 16, VerticalAlignment = VerticalAlignment.Center }) });
        var menu = new StackPanel { Margin = new Thickness(14, 8), Spacing = 4 };
        (string Label, string Icon)[] items = [("Visão geral", "Home"), ("Pessoas e rendas", "Users"), ("Despesas", "List"), ("Previsões", "Trend"), ("Ajustes", "Sliders")];
        for (var i = 0; i < items.Length; i++)
        {
            // Copia o índice para que cada callback navegue à página correspondente, em vez do valor final do laço.
            var index = i; var button = Button(items[i].Label, () => GoTo(index), "nav", items[i].Icon); navigation.Add(button); menu.Children.Add(button);
        }
        Grid.SetRow(menu, 1); sidebar.Children.Add(menu);
        var privacy = Text("Seus dados ficam só neste computador.", "muted"); privacy.FontSize = 12;
        var footerIcon = Icon("Lock", 14); footerIcon.VerticalAlignment = VerticalAlignment.Top; footerIcon.Margin = new Thickness(0, 2, 8, 0);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(24, 16, 24, 24) }; Grid.SetColumn(privacy, 1); footer.Children.Add(footerIcon); footer.Children.Add(privacy);
        Grid.SetRow(footer, 2); sidebar.Children.Add(footer);
        // Conteúdo: aviso global (quando há) e a página rolável, com margem de 32/40 px.
        noticeBox.Classes.Add("notice"); noticeBox.Child = notice; noticeBox.IsVisible = false; noticeBox.Margin = new Thickness(40, 24, 40, 0);
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*") }; body.Children.Add(noticeBox);
        var scroll = new ScrollViewer { Content = new Border { Padding = new Thickness(40, 32), Child = page }, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); body.Children.Add(scroll);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("232,*") }; root.Children.Add(sidebar); Grid.SetColumn(body, 1); root.Children.Add(body); Content = root;
        // Atalho Ctrl+N: nova despesa de qualquer tela (diálogos modais bloqueiam a janela, então não abre dois).
        KeyDown += (_, e) => { if (e.Key == Key.N && e.KeyModifiers == KeyModifiers.Control && ready) { e.Handled = true; _ = Run(() => EditExpense()); } };
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
    private void GoTo(int index) { currentPage = index; Render(); }
    /// <summary>
    /// Cabeçalho comum das páginas: título e subtítulo à esquerda; à direita, navegação de mês e, se pedido, o botão de nova despesa.
    /// </summary>
    private Control PageHeader(string title, string subtitle, bool monthNavigation = true, bool newExpense = false)
    {
        var titles = Stack(Text(title, "title"), Text(subtitle, "muted")); titles.Spacing = 4; titles.VerticalAlignment = VerticalAlignment.Center;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (monthNavigation)
        {
            var label = new TextBlock { Text = month.ToString("MMMM 'de' yyyy", Money.Culture), FontWeight = FontWeight.SemiBold, MinWidth = 130, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(Button("Mês anterior", () => ChangeMonth(-1), icon: "ChevronLeft", iconOnly: true)); actions.Children.Add(label);
            actions.Children.Add(Button("Próximo mês", () => ChangeMonth(1), icon: "ChevronRight", iconOnly: true));
            actions.Children.Add(Button("Hoje", () => { month = Dates.Month(DateOnly.FromDateTime(DateTime.Today)); Render(); }));
        }
        if (newExpense) { var add = Button("Nova despesa", () => _ = Run(() => EditExpense()), "primary", "Plus"); add.Margin = new Thickness(8, 0, 0, 0); actions.Children.Add(add); }
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 24) }; Grid.SetColumn(actions, 1); header.Children.Add(titles); header.Children.Add(actions);
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
    /// Compõe um indicador monetário com rótulo e contexto. O tom ("positive", "negative" ou "accent") escolhe a cor do valor no tema atual.
    /// </summary>
    private static Control Metric(string label, decimal amount, string hint, string? tone = null)
    {
        var value = Text(Money.Format(amount), "money"); if (tone is not null) value.Classes.Add(tone);
        return Card(Stack(Text(label, "muted"), value, Text(hint, "muted")));
    }
    /// <summary>
    /// Produz a mensagem local dos formulários sem exibir stack traces ou detalhes internos ao usuário.
    /// </summary>
    private static string FriendlyError(Exception ex) => ex is BudgetException ? ex.Message : ex is IOException or UnauthorizedAccessException ? "Não foi possível salvar. Verifique espaço em disco, permissões e acesso à pasta. Nenhuma alteração foi aplicada." : "Não foi possível concluir. Revise os campos e tente novamente.";
}
