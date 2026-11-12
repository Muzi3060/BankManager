using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Compartilhar despesas: gerar um arquivo com despesas escolhidas e importar um arquivo desses ADICIONANDO as despesas, sem substituir nada.
public sealed partial class MainWindow
{
    private static readonly FilePickerFileType ShareFiles = new("Despesas compartilhadas do BankManager") { Patterns = ["*" + ShareFile.Extension] };
    // Máximo de despesas listadas para seleção; as mais recentes primeiro.
    private const int MaxShareRows = 2000;
    /// <summary>
    /// Assistente "Compartilhar despesas": marcar as despesas (uma a uma ou por atalhos), ver a prévia do que vai no arquivo e salvar.
    /// O arquivo é de outro tipo que o backup e leva só as despesas marcadas. Público porque os testes de interface o chamam com um
    /// caminho pronto, sem passar pelo seletor de arquivos.
    /// </summary>
    public async Task ShareExpenses(string? presetPath = null)
    {
        if (!ready) throw new BudgetException("Os dados ainda não estão disponíveis.");
        if (data.Expenses.Count == 0) throw new BudgetException("Ainda não há despesas para compartilhar.");
        var names = data.People.ToDictionary(p => p.Id, p => p.Name);
        // As despesas do mês selecionado vêm primeiro (são as mais prováveis de compartilhar); depois as demais, das mais recentes para as mais antigas.
        var rows = data.Expenses.OrderByDescending(e => Dates.Month(e.DueDate) == month).ThenByDescending(e => e.DueDate).ThenBy(e => e.Title).Take(MaxShareRows).Select(e =>
        {
            var detail = $"{e.DueDate:dd/MM/yyyy} · {e.Title} · {e.Category} · {Money.Format(e.Amount)}" + (e.InstallmentCount > 1 ? $" · parcela {e.InstallmentNumber}/{e.InstallmentCount}" : "");
            var check = new CheckBox { Content = detail }; AutomationProperties.SetName(check, "Compartilhar " + e.Title); return (Expense: e, Check: check);
        }).ToList();
        var list = new StackPanel { Spacing = 0 }; foreach (var row in rows) list.Children.Add(row.Check);
        var payment = new CheckBox { Content = "Incluir a situação de pagamento, a data e quem pagou", IsChecked = false }; AutomationProperties.SetName(payment, "Incluir pagamento");
        var preview = Text("", "support"); var error = Text("", "error"); Window? dialog = null;
        var save = Button("Salvar arquivo", () => { }, "primary"); save.IsEnabled = false;
        // Prévia ao vivo: quantidade, total, pessoas citadas e categorias próprias que vão junto.
        void Update()
        {
            var chosen = rows.Where(r => r.Check.IsChecked == true).Select(r => r.Expense).ToList(); save.IsEnabled = chosen.Count > 0; error.Text = "";
            if (chosen.Count == 0) { preview.Text = "Nenhuma despesa marcada ainda."; return; }
            var withPayment = payment.IsChecked == true;
            var people = chosen.SelectMany(e => withPayment && e.PaidBy is { } p ? e.Participants.Append(p) : e.Participants).Distinct().Select(id => names[id]).ToList();
            var categories = chosen.Select(e => e.Category).Distinct().Where(data.CustomCategories.Contains).ToList();
            preview.Text = $"{chosen.Count} {(chosen.Count == 1 ? "despesa" : "despesas")} · total {Money.Format(chosen.Sum(e => e.Amount))}\nPessoas citadas (só o nome): {string.Join(", ", people)}"
                + (categories.Count > 0 ? $"\nCategorias próprias incluídas: {string.Join(", ", categories)}" : "")
                + (withPayment ? "\nCom a situação de pagamento, a data e quem pagou." : "\nTodas vão como “em aberto”, sem data de pagamento nem quem pagou.")
                + "\nNão vão no arquivo: rendas, metas, acertos e as despesas não marcadas.";
        }
        foreach (var row in rows) row.Check.IsCheckedChanged += (_, _) => Update();
        payment.IsCheckedChanged += (_, _) => Update();
        // Atalhos de seleção. Eles só marcam; desmarcar é pela caixa ou por "Limpar seleção".
        var categoryNames = BudgetRules.AllCategories(data).Where(c => rows.Any(r => r.Expense.Category == c)).ToList();
        var category = Combo(new[] { "Marcar uma categoria…" }.Concat(categoryNames), 0); category.MinWidth = 210; category.HorizontalAlignment = HorizontalAlignment.Left; AutomationProperties.SetName(category, "Marcar uma categoria");
        category.SelectionChanged += (_, _) =>
        {
            if (category.SelectedIndex <= 0) return;
            foreach (var row in rows.Where(r => r.Expense.Category == categoryNames[category.SelectedIndex - 1])) row.Check.IsChecked = true;
            category.SelectedIndex = 0;
        };
        var thisMonth = Button("Todas deste mês", () => { foreach (var row in rows.Where(r => Dates.Month(r.Expense.DueDate) == month)) row.Check.IsChecked = true; });
        ToolTip.SetTip(thisMonth, "Marca todas as despesas de " + month.ToString("MMMM 'de' yyyy", Money.Culture));
        var installments = Button("Completar parcelas", () =>
        {
            // Para cada parcela marcada, marca as outras parcelas da mesma compra.
            var groups = rows.Where(r => r.Check.IsChecked == true && r.Expense.InstallmentGroup is not null).Select(r => r.Expense.InstallmentGroup).ToHashSet();
            foreach (var row in rows.Where(r => groups.Contains(r.Expense.InstallmentGroup))) row.Check.IsChecked = true;
        });
        ToolTip.SetTip(installments, "Marca todas as parcelas das compras parceladas que já têm alguma parcela marcada");
        var clear = Button("Limpar seleção", () => { foreach (var row in rows) row.Check.IsChecked = false; }, "ghost");
        var shortcuts = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in new Control[] { thisMonth, category, installments, clear }) { control.Margin = new Thickness(0, 0, 8, 8); shortcuts.Children.Add(control); }
        var listBox = new Border { Child = new ScrollViewer { Content = list, MaxHeight = 260 }, Padding = new Thickness(12, 6) }.WithClass("solid"); listBox.BoxShadow = default; listBox.BorderThickness = new Thickness(1); listBox.Classes.Add("listbox");
        var panel = Stack(Heading("Compartilhar despesas", "Marque as despesas que vão no arquivo. Quem receber importa o arquivo e elas são adicionadas às despesas dela, sem substituir nada."),
            shortcuts, listBox);
        if (data.Expenses.Count > MaxShareRows) panel.Children.Add(Text($"Mostrando {MaxShareRows} despesas: as do mês selecionado e as mais recentes.", "muted"));
        panel.Children.Add(payment); panel.Children.Add(Text("O que vai no arquivo", "section")); panel.Children.Add(preview);
        panel.Children.Add(InlineNotice("Atenção: o arquivo é um texto legível, sem senha, e contém informações financeiras. Envie só para quem deve recebê-lo."));
        panel.Children.Add(error);
        dialog = Dialog("Compartilhar despesas", panel, 780); dialog.Height = 820;
        save.Click += async (_, _) =>
        {
            try
            {
                var chosen = rows.Where(r => r.Check.IsChecked == true).Select(r => r.Expense.Id).ToList();
                var file = ExpenseShare.Build(data, chosen, payment.IsChecked == true);
                var path = presetPath;
                if (path is null)
                {
                    var target = await dialog.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Salvar despesas compartilhadas", SuggestedFileName = $"despesas-{DateTime.Today:yyyy-MM-dd}{ShareFile.Extension}", FileTypeChoices = [ShareFiles] });
                    if (target is null) return;
                    path = target.TryGetLocalPath() ?? throw new BudgetException("Escolha uma pasta local.");
                    // O seletor pode devolver o nome sem a extensão própria; ela identifica o tipo do arquivo.
                    if (!path.EndsWith(ShareFile.Extension, StringComparison.OrdinalIgnoreCase)) path += ShareFile.Extension;
                }
                RejectInternalPath(path); ExpenseShare.Save(path, file);
                Notify($"{file.Expenses.Count} {(file.Expenses.Count == 1 ? "despesa salva" : "despesas salvas")} em {Path.GetFileName(path)}. Envie o arquivo só para quem deve recebê-lo.", success: true); dialog.Close();
            }
            catch (Exception ex) { error.Text = FriendlyError(ex); }
        };
        panel.Children.Add(Row(Button("Cancelar", () => dialog.Close()), save));
        Update();
        await dialog.ShowDialog(this);
    }
    /// <summary>
    /// Pede um arquivo de compartilhamento e abre o assistente de importação.
    /// </summary>
    private async Task ImportShared()
    {
        if (!ready) throw new BudgetException("Os dados ainda não estão disponíveis.");
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Importar despesas compartilhadas", AllowMultiple = false, FileTypeFilter = [ShareFiles, new FilePickerFileType("Arquivos JSON") { Patterns = ["*.json"] }] });
        if (files.Count == 0) return;
        await ImportSharedFromFile(files[0].TryGetLocalPath() ?? throw new BudgetException("Escolha um arquivo local."));
    }
    /// <summary>
    /// Assistente "Importar despesas compartilhadas", em passos numerados na mesma janela: resumo do arquivo, pessoas, categorias,
    /// opções e prévia ao vivo. As despesas são ADICIONADAS em uma única gravação (tudo ou nada); nada existente é alterado.
    /// Um arquivo inválido, ou um backup escolhido por engano, é recusado antes de qualquer tela. Público por causa dos testes de interface.
    /// </summary>
    public async Task ImportSharedFromFile(string path)
    {
        if (!ready || store is null) throw new BudgetException("Os dados ainda não estão disponíveis.");
        // Passo 1: validar (tamanho, profundidade, campos desconhecidos, tipo e versão) antes de mostrar qualquer coisa.
        RejectInternalPath(path); var file = ExpenseShare.Read(path); var suggestion = ExpenseShare.Suggest(data, file);
        var localNames = data.People.Select(p => p.Name).ToList();
        // Passo 2: uma escolha por pessoa do arquivo. Opções: cada pessoa cadastrada, criar um cadastro novo ou ignorar.
        var people = file.People.Select(name =>
        {
            var choice = suggestion.People[name];
            var box = Combo(localNames.Concat([$"Criar o cadastro “{name}”", "Ignorar esta pessoa"]), choice.Action == PersonAction.Existing ? data.People.FindIndex(p => p.Id == choice.PersonId) : localNames.Count);
            AutomationProperties.SetName(box, "Pessoa: " + name); return (Name: name, Box: box);
        }).ToList();
        var fallback = Combo(new[] { "Escolha uma pessoa…" }.Concat(localNames), 0); AutomationProperties.SetName(fallback, "Quem assume as despesas sem responsável");
        var fallbackField = Field("Despesas que ficarem sem responsável vão para", fallback, "Só é usado quando todas as pessoas de uma despesa foram ignoradas.");
        // Passo 3: categorias próprias do arquivo que não existem aqui.
        var categories = ExpenseShare.MissingCategories(data, file).Select(name =>
        {
            var box = Combo([$"Criar a categoria “{name}”", "Usar “Outros”"], suggestion.CreateCategories.GetValueOrDefault(name) ? 0 : 1); AutomationProperties.SetName(box, "Categoria: " + name); return (Name: name, Box: box);
        }).ToList();
        var hasProportional = file.Expenses.Any(e => e.Split == SplitMode.AvailableIncome);
        var equal = new CheckBox { Content = "Importar as despesas proporcionais à renda como divisão igual" }; AutomationProperties.SetName(equal, "Importar como divisão igual");
        var duplicates = new CheckBox { Content = "Importar também as que já existem aqui, como despesas novas" }; AutomationProperties.SetName(duplicates, "Importar duplicadas");
        var preview = Text("", "support"); var error = Text("", "error"); Window? dialog = null; var building = true;
        var import = Button("Adicionar despesas", () => { }, "primary"); import.IsEnabled = false;
        // Lê as escolhas da tela.
        ShareMapping Mapping() => new()
        {
            People = people.ToDictionary(p => p.Name, p => p.Box.SelectedIndex < localNames.Count ? new PersonChoice(PersonAction.Existing, data.People[p.Box.SelectedIndex].Id) : new PersonChoice(p.Box.SelectedIndex == localNames.Count ? PersonAction.Create : PersonAction.Ignore)),
            CreateCategories = categories.ToDictionary(c => c.Name, c => c.Box.SelectedIndex == 0),
            Fallback = fallback.SelectedIndex > 0 ? data.People[fallback.SelectedIndex - 1].Id : null,
            EqualInsteadOfProportional = equal.IsChecked == true, ImportDuplicates = duplicates.IsChecked == true
        };
        // Passo 4: prévia ao vivo, calculada em uma cópia dos dados (nada é gravado aqui).
        void Update()
        {
            if (building || people.Any(p => p.Box.SelectedIndex < 0) || categories.Any(c => c.Box.SelectedIndex < 0) || fallback.SelectedIndex < 0) return;
            fallbackField.IsVisible = people.Any(p => p.Box.SelectedIndex == localNames.Count + 1);
            try
            {
                var (_, result) = ExpenseShare.Import(data, file, Mapping());
                var lines = new List<string> { result.Added.Count == 0 ? "Nenhuma despesa será adicionada." : $"{result.Added.Count} {(result.Added.Count == 1 ? "despesa será adicionada" : "despesas serão adicionadas")} (total {Money.Format(result.Added.Sum(e => e.Amount))}). Nada do que já existe aqui será alterado." };
                if (result.Duplicates > 0) lines.Add($"{result.Duplicates} já {(result.Duplicates == 1 ? "existe" : "existem")} aqui e {(result.Duplicates == 1 ? "será pulada" : "serão puladas")}.");
                if (result.Errors.Count > 0) lines.Add($"{result.Errors.Count} com erro, que não {(result.Errors.Count == 1 ? "será importada" : "serão importadas")}: " + string.Join(" ", result.Errors.Take(3)) + (result.Errors.Count > 3 ? " …" : ""));
                if (result.NewPeople.Count > 0) lines.Add("Pessoas que serão cadastradas: " + string.Join(", ", result.NewPeople) + ". Informe a renda delas depois, em Pessoas e rendas.");
                if (result.NewCategories.Count > 0) lines.Add("Categorias que serão criadas: " + string.Join(", ", result.NewCategories) + ".");
                if (result.Proportional > 0) lines.Add($"Atenção: {result.Proportional} {(result.Proportional == 1 ? "despesa usa" : "despesas usam")} a divisão proporcional à renda. O rateio será recalculado com as rendas cadastradas AQUI e pode ser diferente do de quem enviou.");
                lines.AddRange(result.Added.Take(5).Select(e => $"• {e.DueDate:dd/MM/yyyy}  {e.Title}  {Money.Format(e.Amount)}"));
                if (result.Added.Count > 5) lines.Add($"… e mais {result.Added.Count - 5}.");
                preview.Text = string.Join("\n", lines); error.Text = ""; import.IsEnabled = result.Added.Count > 0;
            }
            catch (Exception ex) { preview.Text = ""; error.Text = FriendlyError(ex); import.IsEnabled = false; }
        }
        foreach (var box in people.Select(p => p.Box).Concat(categories.Select(c => c.Box)).Append(fallback)) box.SelectionChanged += (_, _) => Update();
        equal.IsCheckedChanged += (_, _) => Update(); duplicates.IsCheckedChanged += (_, _) => Update();
        var total = file.Expenses.Sum(e => e.Amount);
        var panel = Stack(Heading("Importar despesas compartilhadas", "As despesas do arquivo são ADICIONADAS às suas. Nada é substituído nem apagado."),
            Text("1. Arquivo", "section"), Text($"{Path.GetFileName(path)} · {file.Expenses.Count} {(file.Expenses.Count == 1 ? "despesa" : "despesas")} · total {Money.Format(total)} · pessoas citadas: {string.Join(", ", file.People)}", "muted"),
            Text("2. Pessoas", "section"), Text("Diga quem é cada pessoa do arquivo na sua base. A sugestão compara os nomes; confira antes de continuar.", "muted"));
        // Field usa o rótulo como nome acessível; o nome curto ("Pessoa: Ana") é reposto em seguida.
        foreach (var (name, box) in people) { panel.Children.Add(Field($"“{name}” no arquivo é", box)); AutomationProperties.SetName(box, "Pessoa: " + name); }
        panel.Children.Add(fallbackField);
        if (categories.Count > 0)
        {
            panel.Children.Add(Text("3. Categorias", "section")); panel.Children.Add(Text($"Estas categorias do arquivo não existem aqui. Você pode criar (até {CategoryEngine.MaxCustom} categorias próprias) ou usar “Outros”.", "muted"));
            foreach (var (name, box) in categories) { panel.Children.Add(Field($"Categoria “{name}”", box)); AutomationProperties.SetName(box, "Categoria: " + name); }
        }
        panel.Children.Add(Text(categories.Count > 0 ? "4. Opções" : "3. Opções", "section"));
        if (hasProportional) panel.Children.Add(Stack(equal, Text("A divisão proporcional depende da renda de cada pessoa na SUA base. Mantendo esse modo, o rateio é recalculado com as rendas daqui; marcando a opção, a despesa é dividida em partes iguais.", "muted")));
        panel.Children.Add(duplicates);
        panel.Children.Add(Text(categories.Count > 0 ? "5. O que será adicionado" : "4. O que será adicionado", "section")); panel.Children.Add(preview); panel.Children.Add(error);
        dialog = Dialog("Importar despesas compartilhadas", panel, 780); dialog.Height = 860;
        import.Click += (_, _) =>
        {
            try
            {
                // Uma única gravação: a alteração é feita em uma cópia, o orçamento inteiro é validado e só então o arquivo é trocado
                // (a versão anterior vira a cópia automática). Qualquer falha deixa tudo como estava.
                ShareResult? result = null; var mapping = Mapping();
                Commit(d => result = ExpenseShare.Apply(d, file, mapping));
                var skipped = result!.Duplicates + result.Errors.Count;
                Notify($"{result.Added.Count} {(result.Added.Count == 1 ? "despesa adicionada" : "despesas adicionadas")}, {skipped} {(skipped == 1 ? "pulada" : "puladas")}.", success: true); dialog.Close();
            }
            catch (Exception ex) { error.Text = FriendlyError(ex); }
        };
        panel.Children.Add(Row(Button("Cancelar", () => dialog.Close()), import));
        building = false; Update();
        await dialog.ShowDialog(this);
    }
}
