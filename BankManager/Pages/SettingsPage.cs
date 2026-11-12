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
        foreach (var button in new[] { Button("Exportar backup", () => _ = Run(Export), "ink"), Button("Importar backup", () => _ = Run(Import)), Button("Recuperar versão anterior", () => _ = Run(Recover)) }) { button.Margin = new Thickness(0, 0, 8, 8); wrap.Children.Add(button); }
        var folder = Text(store?.DirectoryPath ?? "—", "muted"); folder.FontSize = 12;
        // Planilhas: exportar as despesas e importar um extrato bancário, ambos em CSV.
        var sheets = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { Button("Exportar despesas (CSV)", () => _ = Run(ExportCsv)), Button("Importar extrato (CSV)", () => _ = Run(ImportStatement)) }) { button.Margin = new Thickness(0, 0, 8, 8); sheets.Children.Add(button); }
        var yourData = Card(Stack(Text("Seus dados", "section"), Text("Tudo fica salvo só neste computador. Exporte um backup de vez em quando e guarde em outro lugar, como um pendrive.", "muted"), Text("Pasta dos dados", "muted"), folder, wrap,
            Text("Planilhas", "muted"), sheets));
        var help = Card(Stack(Text("Ajuda", "section"),
            Collapsible("Como funciona a divisão", "Individual: uma pessoa paga tudo.\nIgual: o valor é dividido igualmente entre os selecionados.\nProporcional: quem tem mais renda disponível assume uma parcela maior. Despesas individuais, pagas ou em aberto, são descontadas da renda antes da divisão. Todas as despesas proporcionais do mês usam essa mesma base.\n\nExemplo: rendas disponíveis de R$ 3.000 e R$ 1.000 dividem uma conta de R$ 800 em R$ 600 e R$ 200. Quem não tem renda disponível recebe peso zero. Se todos tiverem peso zero, a conta é dividida igualmente, com aviso. Centavos restantes são distribuídos sem alterar o total."),
            Divider(),
            Collapsible("Dívidas, parcelas e listas", "Para uma dívida, informe o valor total já com os juros negociados e o número de parcelas. A primeira data é o primeiro vencimento. O aplicativo distribui o total; não calcula taxas bancárias.\n\nUma lista soma quantidade × preço de cada item. Não cadastre a mesma compra novamente como outra despesa. Marcar como pago quita o lançamento inteiro para todos os responsáveis; pagamentos parciais não são registrados nesta versão.\n\nO saldo planejado considera todas as despesas do mês, inclusive as pagas. Parcelas são editadas e excluídas individualmente."),
            Divider(),
            Collapsible("Privacidade e backups", "O aplicativo funciona sem internet e não envia nenhum dado para fora. Cada salvamento guarda uma cópia da versão anterior, que você pode recuperar aqui ao lado.\n\nOs arquivos não são criptografados: backups contêm informações financeiras em texto legível. Guarde-os em um local privado e não os envie a quem você não conhece.")));
        ((StackPanel)help.Child!).Spacing = 4;
        var below = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") }; Grid.SetColumn(help, 2); below.Children.Add(yourData); below.Children.Add(help); yourData.VerticalAlignment = VerticalAlignment.Top; help.VerticalAlignment = VerticalAlignment.Top;
        var page = Stack(PageHeader("Ajustes", "Aparência, categorias, seus dados e ajuda.", monthNavigation: false), appearance, CategoriesCard(), below); page.Spacing = 18; return page;
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
    /// Miniatura de uma tela (menu, cartão translúcido e cartão escuro sobre o degradê) desenhada no tema pedido, independentemente do tema atual.
    /// </summary>
    private static Control ThemeMini(ThemeVariant variant) => new ThemeVariantScope { RequestedThemeVariant = variant, Child = new Border { Child = MiniScreen(true), Height = 76 }.WithClass("mini-frame") };
    /// <summary>
    /// Conteúdo da miniatura: fundo em degradê, barras do menu (opcionais) e dois cartões.
    /// </summary>
    private static Control MiniScreen(bool menu)
    {
        var bars = new StackPanel { Spacing = 4, Margin = new Thickness(7, 9, 0, 0), Width = 22 }; foreach (var width in new[] { 16d, 12d, 14d }) bars.Children.Add(MiniBar("mini-muted", width));
        var card = new Border { Child = Stack(MiniBar("mini-text", 26), MiniBar("mini-accent", 18)), Padding = new Thickness(6), Margin = new Thickness(0, 0, 5, 0) }.WithClass("mini-card"); ((StackPanel)card.Child!).Spacing = 5;
        var dark = new Border().WithClass("mini-dark"); Grid.SetColumn(dark, 1);
        var cards = new Grid { ColumnDefinitions = new ColumnDefinitions("1.7*,*"), Margin = new Thickness(8, 10, 8, 10), Children = { card, dark } }; Grid.SetColumn(cards, 1);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(menu ? "Auto,*" : "0,*"), Children = { bars, cards } }; bars.IsVisible = menu;
        return new Border { Child = grid }.WithClass("mini-bg");
    }
    /// <summary>
    /// Miniatura do modo automático: metade clara, metade escura.
    /// </summary>
    private static Control AutoThemeMini()
    {
        var left = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Light, Child = MiniScreen(true) };
        var right = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Dark, Child = MiniScreen(false) };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") }; Grid.SetColumn(right, 1); grid.Children.Add(left); grid.Children.Add(right);
        return new Border { Child = grid, Height = 76 }.WithClass("mini-frame");
    }
    /// <summary>
    /// Barra arredondada da miniatura; a cor vem da classe ("mini-text", "mini-muted" ou "mini-accent").
    /// </summary>
    private static Border MiniBar(string style, double width) { var bar = new Border { Width = width }; bar.Classes.Add(style); return bar; }
    /// <summary>
    /// Cartão de categorias: as padrão (fixas) e as suas, que podem ser criadas, renomeadas e excluídas.
    /// </summary>
    private Control CategoriesCard()
    {
        var builtIn = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var name in BudgetRules.Categories) { var chip = Chip(name, "status"); chip.Margin = new Thickness(0, 0, 6, 6); builtIn.Children.Add(chip); }
        var own = new StackPanel();
        foreach (var name in data.CustomCategories)
        {
            if (own.Children.Count > 0) own.Children.Add(Divider());
            var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
            var rename = Button("Renomear " + name, () => _ = Run(async () =>
            {
                var renamed = await Prompt(this, "Renomear categoria", "Novo nome", name, CategoryEngine.MaxLength);
                if (renamed is not null) Commit(d => CategoryEngine.Rename(d, name, renamed));
            }), "ghost round", "Edit", iconOnly: true);
            var remove = Button("Excluir " + name, () => _ = Run(async () =>
            {
                if (await Confirm(this, "Excluir categoria?", $"Excluir a categoria “{name}”? Só é possível se nenhuma despesa ou recorrência a usar.")) Commit(d => CategoryEngine.Remove(d, name));
            }), "ghost round danger", "Trash", iconOnly: true);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 4) };
            Grid.SetColumn(rename, 1); Grid.SetColumn(remove, 2); row.Children.Add(label); row.Children.Add(rename); row.Children.Add(remove); own.Children.Add(row);
        }
        if (data.CustomCategories.Count == 0) own.Children.Add(Text("Você ainda não criou categorias próprias.", "muted"));
        var input = Input(null, "Nova categoria", CategoryEngine.MaxLength); AutomationProperties.SetName(input, "Nova categoria");
        var add = Button("Adicionar categoria", () => _ = Run(() => { Commit(d => CategoryEngine.Add(d, input.Text ?? "")); return Task.CompletedTask; }), icon: "Plus");
        var form = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,Auto") }; Grid.SetColumn(add, 2); form.Children.Add(input); form.Children.Add(add);
        return Card(Stack(Text("Categorias", "section"), Text("As categorias padrão não mudam. Crie as suas para organizar melhor, por exemplo Pets ou Viagens.", "muted"), builtIn, own, form));
    }
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
        store.Save(imported); data = imported; ready = true; TopUpRecurrences(); Render();
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
        store.Save(backup); data = backup; ready = true; TopUpRecurrences(); Render();
    }
}
