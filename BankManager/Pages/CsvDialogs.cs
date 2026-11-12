using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Planilhas: exportar as despesas em CSV e importar um extrato bancário em CSV, com um assistente de prévia.
public sealed partial class MainWindow
{
    private static readonly FilePickerFileType CsvFiles = new("Planilha CSV") { Patterns = ["*.csv", "*.txt"] };
    /// <summary>
    /// Pede onde salvar e grava todas as despesas em CSV (Excel em português: ponto e vírgula, vírgula decimal, UTF-8 com BOM).
    /// </summary>
    private async Task ExportCsv()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Exportar despesas", SuggestedFileName = $"despesas-{DateTime.Today:yyyy-MM-dd}.csv", DefaultExtension = "csv", FileTypeChoices = [new FilePickerFileType("Planilha CSV") { Patterns = ["*.csv"] }] });
        if (file is null) return;
        var path = file.TryGetLocalPath() ?? throw new BudgetException("Escolha uma pasta local.");
        RejectInternalPath(path); CsvExport.Save(path, CsvExport.Expenses(data));
        Notify($"{data.Expenses.Count} despesas exportadas.", success: true);
    }
    /// <summary>
    /// Pede um arquivo e abre o assistente de importação de extrato.
    /// </summary>
    private async Task ImportStatement()
    {
        if (!ready) throw new BudgetException("Os dados ainda não estão disponíveis.");
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Importar extrato", AllowMultiple = false, FileTypeFilter = [CsvFiles] });
        if (files.Count == 0) return;
        await ImportStatementFromFile(files[0].TryGetLocalPath() ?? throw new BudgetException("Escolha um arquivo local."));
    }
    /// <summary>
    /// Lê o CSV e abre o assistente: escolher as colunas (já sugeridas), o sinal das despesas e o responsável, vendo ao vivo o que será
    /// importado. Só grava ao confirmar. Público porque os testes de interface o chamam sem passar pelo seletor de arquivos.
    /// </summary>
    public async Task ImportStatementFromFile(string path)
    {
        var table = Csv.Parse(StatementImport.ReadFile(path)); var guess = StatementImport.Guess(table); var width = table.Rows.Max(r => r.Count);
        var header = new CheckBox { Content = "A primeira linha é o cabeçalho (nomes das colunas)", IsChecked = guess.HasHeader };
        // Nome de cada coluna: o do cabeçalho; sem cabeçalho, um exemplo do conteúdo para a pessoa reconhecer a coluna.
        List<string> ColumnNames() => Enumerable.Range(0, width).Select(c => header.IsChecked == true ? $"Coluna {c + 1}: {(c < table.Rows[0].Count ? table.Rows[0][c] : "")}" : $"Coluna {c + 1} (ex.: {(c < table.Rows[0].Count ? table.Rows[0][c] : "")})").ToList();
        var date = Combo(ColumnNames(), guess.DateColumn); var description = Combo(ColumnNames(), guess.DescriptionColumn); var amount = Combo(ColumnNames(), guess.AmountColumn);
        var sign = Combo(["Negativos (extrato de conta corrente)", "Positivos (fatura de cartão)"], 0);
        // Responsável: uma pessoa (despesa individual) ou, havendo mais de uma pessoa, todas com divisão igual ou pela renda disponível.
        var who = data.People.Select(p => p.Name).ToList(); if (data.People.Count > 1) { who.Add("Todos, em partes iguais"); who.Add("Todos, pela renda disponível"); }
        var responsible = Combo(who, 0);
        var preview = Text("", "muted"); var error = Text("", "error"); Window? dialog = null; ImportPlan? plan = null; var building = true;
        var import = Button("Importar", () => { }, "primary"); import.IsEnabled = false;
        // Recalcula o plano com as escolhas atuais e mostra o resultado; sem despesas a importar, o botão fica desligado.
        void Update()
        {
            if (building || date.SelectedIndex < 0 || description.SelectedIndex < 0 || amount.SelectedIndex < 0 || responsible.SelectedIndex < 0) return;
            try
            {
                var index = responsible.SelectedIndex; var everyone = data.People.Select(p => p.Id).ToList();
                var (split, people) = index < data.People.Count ? (SplitMode.Individual, [data.People[index].Id]) : (index == data.People.Count ? SplitMode.Equal : SplitMode.AvailableIncome, everyone);
                plan = StatementImport.Plan(data, table, new ImportMapping(date.SelectedIndex, description.SelectedIndex, amount.SelectedIndex, header.IsChecked == true, sign.SelectedIndex == 0 ? AmountSign.NegativeIsExpense : AmountSign.PositiveIsExpense), split, people);
                var lines = new List<string> { $"{plan.Expenses.Count} {(plan.Expenses.Count == 1 ? "despesa será importada" : "despesas serão importadas")} (já pagas, na categoria Outros)." };
                if (plan.IgnoredEntries > 0) lines.Add($"{plan.IgnoredEntries} linha(s) ignorada(s): entradas, estornos ou valor zero.");
                if (plan.Duplicates > 0) lines.Add($"{plan.Duplicates} linha(s) repetida(s): já existem com a mesma data, valor e descrição.");
                if (plan.ErrorCount > 0) lines.Add($"{plan.ErrorCount} linha(s) com erro, que não serão importadas: " + string.Join("; ", plan.Errors.Take(3)) + (plan.ErrorCount > 3 ? "…" : ""));
                lines.AddRange(plan.Expenses.Take(5).Select(e => $"• {e.DueDate:dd/MM/yyyy}  {e.Title}  {Money.Format(e.Amount)}"));
                if (plan.Expenses.Count > 5) lines.Add($"… e mais {plan.Expenses.Count - 5}.");
                preview.Text = string.Join("\n", lines); error.Text = ""; import.IsEnabled = plan.Expenses.Count > 0;
            }
            catch (Exception ex) { plan = null; preview.Text = ""; error.Text = FriendlyError(ex); import.IsEnabled = false; }
        }
        header.IsCheckedChanged += (_, _) =>
        {
            // Os nomes das colunas mudam com o cabeçalho; troca as listas sem perder as escolhas.
            building = true; foreach (var box in new[] { date, description, amount }) { var keep = box.SelectedIndex; box.ItemsSource = ColumnNames(); box.SelectedIndex = keep; }
            building = false; Update();
        };
        foreach (var box in new[] { date, description, amount, sign, responsible }) box.SelectionChanged += (_, _) => Update();
        AutomationProperties.SetName(date, "Coluna da data"); AutomationProperties.SetName(description, "Coluna da descrição"); AutomationProperties.SetName(amount, "Coluna do valor"); AutomationProperties.SetName(responsible, "Responsável");
        var panel = Stack(Heading("Importar extrato", $"{Path.GetFileName(path)} · {table.Rows.Count} linhas. Confira as colunas: cada saída vira uma despesa já paga."), header,
            Columns(Field("Data", date), Field("Descrição", description)), Columns(Field("Valor", amount), Field("Valores que são despesas", sign)),
            Field("Responsável pelas despesas", responsible, "Você pode editar cada despesa depois, por exemplo para mudar a categoria."), Text("O que será importado", "section"), preview, error);
        dialog = Dialog("Importar extrato", panel, 760); dialog.Height = 820;
        import.Click += (_, _) =>
        {
            if (plan is null || plan.Expenses.Count == 0) return;
            var created = plan.Expenses; var count = created.Count;
            try { Commit(d => d.Expenses.AddRange(created)); Notify($"{count} {(count == 1 ? "despesa importada" : "despesas importadas")}. Edite cada uma para escolher a categoria.", success: true); dialog.Close(); }
            catch (Exception ex) { error.Text = FriendlyError(ex); }
        };
        panel.Children.Add(Row(Button("Cancelar", () => dialog.Close()), import));
        building = false; Update();
        await dialog.ShowDialog(this);
    }
}
