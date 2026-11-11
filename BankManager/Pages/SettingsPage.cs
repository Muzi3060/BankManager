using Avalonia.Controls;
using Avalonia.Platform.Storage;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Página de ajustes: aparência, backup e recuperação dos dados e textos de ajuda.
public sealed partial class MainWindow
{
    /// <summary>
    /// Reúne aparência, exportação, importação, recuperação e explicações acessíveis sobre as regras e limites do aplicativo.
    /// </summary>
    private Control SettingsPage()
    {
        // A ordem das opções coincide com ThemePreference (Auto, Light, Dark), então o índice vira o valor do enum.
        var theme = Combo(["Automático (segue o sistema)", "Claro", "Escuro"], (int)settings.Theme);
        theme.SelectionChanged += (_, _) => ChangeTheme((ThemePreference)theme.SelectedIndex);
        return Stack(Heading("Seus dados, sob seu controle", "O aplicativo funciona localmente e não exige cadastro nem conexão com a internet."),
            Card(Stack(Text("Aparência", "section"), Text("Escolha o tema do aplicativo. A opção automática acompanha o tema do seu computador.", "muted"), Field("Tema", theme))),
            Card(Stack(Text("Backup e recuperação", "section"), Text("Cada salvamento mantém uma cópia da versão anterior. Exporte backups regularmente para outra pasta ou dispositivo. Os arquivos contêm informações financeiras em texto legível: guarde-os em um local privado.", "muted"),
                Row(Button("Exportar backup", () => _ = Run(Export), "primary"), Button("Importar backup", () => _ = Run(Import))), Button("Recuperar versão anterior", () => _ = Run(Recover)), Text($"Pasta local: {store?.DirectoryPath}", "muted"))),
            Card(Stack(Text("Entenda a divisão", "section"), Text("Individual: uma pessoa paga tudo.\nIgual: o valor é dividido igualmente entre os selecionados.\nProporcional: quem tem mais renda disponível assume uma parcela maior. Despesas individuais, pagas ou em aberto, são descontadas da renda antes da divisão. Todas as despesas proporcionais do mês usam essa mesma base.\n\nExemplo: rendas disponíveis de R$ 3.000 e R$ 1.000 dividem uma conta de R$ 800 em R$ 600 e R$ 200. Quem não tem renda disponível recebe peso zero. Se todos tiverem peso zero, a conta é dividida igualmente, com aviso. Centavos restantes são distribuídos sem alterar o total.", "muted"))),
            Card(Stack(Text("Dívidas, listas e histórico", "section"), Text("Para uma dívida, informe o valor total já com os juros negociados e o número de parcelas. A primeira data é o primeiro vencimento. O aplicativo distribui o total; não calcula taxas bancárias.\n\nUma lista soma quantidade × preço de cada item. Não cadastre a mesma compra novamente como outra despesa. Marcar como pago quita o lançamento inteiro para todos os responsáveis; pagamentos parciais não são registrados nesta versão.\n\nO saldo planejado considera todas as despesas do mês, inclusive as pagas. Alterar rendas ou despesas individuais recalcula o rateio daquele mês. Parcelas são editadas e excluídas individualmente.", "muted"))));
    }
    /// <summary>
    /// Aplica o tema na hora e o guarda em settings.json. Se o disco falhar, o tema vale na sessão e o usuário é avisado.
    /// </summary>
    private void ChangeTheme(ThemePreference preference)
    {
        if (preference == settings.Theme) return;
        settings = settings with { Theme = preference }; App.ApplyTheme(preference);
        try { notice.IsVisible = false; SettingsStore.Save(settingsDirectory, settings); }
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
        RejectInternalPath(path); LocalStore.Export(path, data); notice.Text = "Backup exportado com sucesso."; notice.IsVisible = true;
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
