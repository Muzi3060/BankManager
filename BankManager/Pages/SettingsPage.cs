using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Platform.Storage;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Página de ajustes: aparência, backup e recuperação dos dados e textos de ajuda.
public sealed partial class MainWindow
{
    /// <summary>
    /// Ajustes: escolha do tema (cartões com miniatura), seus dados (pasta, backup, recuperação) e ajuda em seções que abrem e fecham.
    /// </summary>
    private Control SettingsPage()
    {
        var themes = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*,12,*") };
        Control[] cards = [ThemeCard("Claro", "Fundo claro", ThemePreference.Light, ThemeMini(ThemeVariant.Light)), ThemeCard("Escuro", "Fundo escuro", ThemePreference.Dark, ThemeMini(ThemeVariant.Dark)), ThemeCard("Automático", "Segue o sistema", ThemePreference.Auto, AutoThemeMini())];
        for (var i = 0; i < cards.Length; i++) { Grid.SetColumn(cards[i], i * 2); themes.Children.Add(cards[i]); }
        var appearance = Card(Stack(Text("Aparência", "section"), Text("Escolha como o aplicativo aparece. A opção automática acompanha o tema do seu computador.", "muted"), themes));
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { Button("Exportar backup", () => _ = Run(Export), "primary"), Button("Importar backup", () => _ = Run(Import)), Button("Recuperar versão anterior", () => _ = Run(Recover)) }) { button.Margin = new Thickness(0, 0, 8, 8); wrap.Children.Add(button); }
        var folder = Text(store?.DirectoryPath ?? "—", "muted"); folder.FontSize = 12;
        var yourData = Card(Stack(Text("Seus dados", "section"), Text("Tudo fica salvo só neste computador. Exporte um backup de vez em quando e guarde em outro lugar, como um pendrive.", "muted"), Text("Pasta dos dados", "muted"), folder, wrap));
        var help = Card(Stack(Text("Ajuda", "section"),
            Collapsible("Como funciona a divisão", "Individual: uma pessoa paga tudo.\nIgual: o valor é dividido igualmente entre os selecionados.\nProporcional: quem tem mais renda disponível assume uma parcela maior. Despesas individuais, pagas ou em aberto, são descontadas da renda antes da divisão. Todas as despesas proporcionais do mês usam essa mesma base.\n\nExemplo: rendas disponíveis de R$ 3.000 e R$ 1.000 dividem uma conta de R$ 800 em R$ 600 e R$ 200. Quem não tem renda disponível recebe peso zero. Se todos tiverem peso zero, a conta é dividida igualmente, com aviso. Centavos restantes são distribuídos sem alterar o total."),
            Divider(),
            Collapsible("Dívidas, parcelas e listas", "Para uma dívida, informe o valor total já com os juros negociados e o número de parcelas. A primeira data é o primeiro vencimento. O aplicativo distribui o total; não calcula taxas bancárias.\n\nUma lista soma quantidade × preço de cada item. Não cadastre a mesma compra novamente como outra despesa. Marcar como pago quita o lançamento inteiro para todos os responsáveis; pagamentos parciais não são registrados nesta versão.\n\nO saldo planejado considera todas as despesas do mês, inclusive as pagas. Parcelas são editadas e excluídas individualmente."),
            Divider(),
            Collapsible("Privacidade e backups", "O aplicativo funciona sem internet e não envia nenhum dado para fora. Cada salvamento guarda uma cópia da versão anterior, que você pode recuperar aqui ao lado.\n\nOs arquivos não são criptografados: backups contêm informações financeiras em texto legível. Guarde-os em um local privado e não os envie a quem você não conhece.")));
        ((StackPanel)help.Child!).Spacing = 4;
        var below = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") }; Grid.SetColumn(help, 2); below.Children.Add(yourData); below.Children.Add(help); yourData.VerticalAlignment = VerticalAlignment.Top; help.VerticalAlignment = VerticalAlignment.Top;
        var page = Stack(PageHeader("Ajustes", "Aparência, seus dados e ajuda.", monthNavigation: false), appearance, below); page.Spacing = 20; return page;
    }
    /// <summary>
    /// Cartão clicável de tema: miniatura, nome e destaque na opção atual. Clicar aplica o tema na hora.
    /// </summary>
    private Control ThemeCard(string title, string subtitle, ThemePreference value, Control preview)
    {
        var label = Stack(new TextBlock { Text = title, FontWeight = FontWeight.Medium }, Text(subtitle, "muted")); label.Spacing = 0;
        var card = new Button { Content = Stack(preview, label) }; card.Classes.Add("themecard"); card.Classes.Set("selected", settings.Theme == value);
        ((StackPanel)card.Content).Spacing = 10;
        AutomationProperties.SetName(card, "Tema " + title.ToLowerInvariant());
        card.Click += (_, _) => { ChangeTheme(value); Render(); };
        return card;
    }
    /// <summary>
    /// Miniatura de uma tela (menu lateral, título e um cartão) desenhada no tema pedido, independentemente do tema atual.
    /// </summary>
    private static Control ThemeMini(ThemeVariant variant)
    {
        var bars = new StackPanel { Spacing = 4, Margin = new Thickness(6, 8) }; foreach (var width in new[] { 14d, 12d, 14d }) bars.Children.Add(MiniBar("mini-muted", width));
        var side = new Border { Child = bars, Width = 28 }; side.Classes.Add("mini-side");
        var inner = Stack(MiniBar("mini-text", 46), new Border { Child = Stack(MiniBar("mini-accent", 26), MiniBar("mini-muted", 40)), Padding = new Thickness(6) }.WithClass("mini-card")); ((StackPanel)inner).Spacing = 6;
        var content = new Border { Child = inner, Padding = new Thickness(8) }; content.Classes.Add("mini-bg");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*") }; Grid.SetColumn(content, 1); grid.Children.Add(side); grid.Children.Add(content);
        return new ThemeVariantScope { RequestedThemeVariant = variant, Child = new Border { Child = grid, Height = 76 }.WithClass("mini-frame") };
    }
    /// <summary>
    /// Miniatura do modo automático: metade clara, metade escura.
    /// </summary>
    private static Control AutoThemeMini()
    {
        var light = new Border { Height = 76 }.WithClass("mini-bg"); var lightSide = new Border { Width = 28, HorizontalAlignment = HorizontalAlignment.Left }.WithClass("mini-side");
        var dark = new Border { Height = 76, Padding = new Thickness(8) }.WithClass("mini-bg"); dark.Child = new Border { Padding = new Thickness(6), Child = Stack(MiniBar("mini-accent", 26), MiniBar("mini-muted", 30)), VerticalAlignment = VerticalAlignment.Bottom }.WithClass("mini-card");
        var left = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Light, Child = new Grid { Children = { light, lightSide } } };
        var right = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Dark, Child = dark };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") }; Grid.SetColumn(right, 1); grid.Children.Add(left); grid.Children.Add(right);
        return new Border { Child = grid, Height = 76 }.WithClass("mini-frame");
    }
    /// <summary>
    /// Barra arredondada da miniatura; a cor vem da classe ("mini-text", "mini-muted" ou "mini-accent").
    /// </summary>
    private static Border MiniBar(string style, double width) { var bar = new Border { Width = width }; bar.Classes.Add(style); return bar; }
    /// <summary>
    /// Aplica o tema na hora e o guarda em settings.json. Se o disco falhar, o tema vale na sessão e o usuário é avisado.
    /// </summary>
    private void ChangeTheme(ThemePreference preference)
    {
        if (preference == settings.Theme) return;
        settings = settings with { Theme = preference }; App.ApplyTheme(preference);
        try { ClearNotice(); SettingsStore.Save(settingsDirectory, settings); }
        catch (Exception ex) { ShowError(ex is IOException or UnauthorizedAccessException ? new BudgetException("O tema foi trocado, mas não foi possível guardar a escolha. Ela volta ao padrão na próxima abertura.") : ex); }
    }
    /// <summary>
    /// Solicita um arquivo local e grava uma cópia validada dos dados atuais fora da pasta interna.
    /// </summary>
    private async Task Export()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Exportar backup", SuggestedFileName = $"bankmanager-{DateTime.Today:yyyy-MM-dd}.json", DefaultExtension = "json", FileTypeChoices = [new FilePickerFileType("Backup JSON") { Patterns = ["*.json"] }] });
        // Fechar o seletor de arquivo equivale a cancelar; nada é gravado.
        if (file is null) return;
        var path = file.TryGetLocalPath() ?? throw new BudgetException("Escolha uma pasta local.");
        RejectInternalPath(path); LocalStore.Export(path, data); Notify("Backup exportado com sucesso.", success: true);
    }
    /// <summary>
    /// Rejeita arquivos diretamente na pasta interna para proteger a base, o backup e o bloqueio de acesso.
    /// </summary>
    private void RejectInternalPath(string path)
    {
        if (store is not null && string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(store.DirectoryPath)), StringComparison.OrdinalIgnoreCase))
            throw new BudgetException("Escolha um arquivo fora da pasta interna do aplicativo.");
    }
    /// <summary>
    /// Valida o backup selecionado antes da confirmação e substitui o estado somente depois de salvar com sucesso.
    /// </summary>
    private async Task Import()
    {
        if (store is null) throw new BudgetException("Feche outra instância do aplicativo antes de importar dados.");
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Importar backup", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Backup JSON") { Patterns = ["*.json"] }] });
        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath() ?? throw new BudgetException("Escolha um arquivo local.");
        // Lê e valida todo o arquivo antes de pedir autorização para substituir o histórico.
        RejectInternalPath(path); var imported = LocalStore.Read(path);
        if (!await Confirm(this, "Substituir os dados atuais?", $"Backup validado: {imported.People.Count} pessoa(s), {imported.Expenses.Count} despesa(s). A importação substitui todo o histórico atual. A versão anterior fica na cópia automática.")) return;
        store.Save(imported); data = imported; ready = true; Render();
    }
    /// <summary>
    /// Lê a cópia automática, confirma a substituição e salva seu conteúdo como base atual antes de atualizar a tela.
    /// </summary>
    private async Task Recover()
    {
        if (store is null || !File.Exists(store.BackupPath)) throw new BudgetException("Não há cópia automática disponível. Você pode importar um backup exportado anteriormente.");
        var backup = LocalStore.Read(store.BackupPath);
        if (!await Confirm(this, "Recuperar versão anterior?", $"Restaurar {backup.People.Count} pessoa(s) e {backup.Expenses.Count} despesa(s)? Os dados atuais serão substituídos.")) return;
        // Save mantém a base substituída como cópia anterior; a recuperação também segue a gravação normal.
        store.Save(backup); data = backup; ready = true; Render();
    }
}
