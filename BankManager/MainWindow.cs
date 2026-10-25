using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
public sealed class MainWindow : Window
{
    private BudgetData data = new();
    private LocalStore? store;
    private bool ready;
    private DateOnly month = Dates.Month(DateOnly.FromDateTime(DateTime.Today));
    private readonly ContentControl page = new();
    private readonly TextBlock monthLabel = Text("");
    private readonly TextBlock notice = Text("");
    private readonly List<Button> navigation = [];
    private int currentPage;
    private string filter = "";
    private int statusFilter;
    private int categoryFilter;
    public MainWindow()
    {
        Title = "BankManager · Finanças em equilíbrio"; Width = 1220; Height = 840; MinWidth = 960; MinHeight = 640;
        var sidebar = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Background = Brush.Parse("#171C26") };
        sidebar.Children.Add(new Border { Padding = new Thickness(24, 30), Child = Stack(Text("◈  BankManager", "section"), Text("Finanças em equilíbrio", "muted")) });
        var menu = new StackPanel { Margin = new Thickness(14, 16), Spacing = 4 };
        string[] labels = ["⌂   Visão geral", "♙   Pessoas e rendas", "☷   Despesas", "↗   Previsões", "⚙   Dados e ajuda"];
        for (var i = 0; i < labels.Length; i++)
        {
            var index = i; var button = Button(labels[i], () => { currentPage = index; Render(); }, "nav"); navigation.Add(button); menu.Children.Add(button);
        }
        Grid.SetRow(menu, 1); sidebar.Children.Add(menu);
        var footer = new Border { Padding = new Thickness(24), Child = Stack(Text("●  Armazenamento local", "muted"), Text("Seu planejamento,\nno seu computador.", "muted")) };
        Grid.SetRow(footer, 2); sidebar.Children.Add(footer);
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), Margin = new Thickness(32, 24) };
        monthLabel.VerticalAlignment = VerticalAlignment.Center; monthLabel.MinWidth = 160; monthLabel.TextAlignment = TextAlignment.Center;
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 20) };
        var months = Row(Button("‹", () => ChangeMonth(-1)), monthLabel, Button("›", () => ChangeMonth(1)), Button("Hoje", () => { month = Dates.Month(DateOnly.FromDateTime(DateTime.Today)); Render(); }));
        DockPanel.SetDock(months, Dock.Right); header.Children.Add(months); header.Children.Add(Text("MEU PLANEJAMENTO", "muted")); body.Children.Add(header);
        notice.Foreground = Brush.Parse("#F6D58C"); notice.Margin = new Thickness(0, 0, 0, 16); notice.IsVisible = false; Grid.SetRow(notice, 1); body.Children.Add(notice);
        var scroll = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 2); body.Children.Add(scroll);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("224,*") }; root.Children.Add(sidebar); Grid.SetColumn(body, 1); root.Children.Add(body); Content = root;
        try
        {
            store = new LocalStore(Environment.GetEnvironmentVariable("BANKMANAGER_DATA_DIR"));
            data = store.Load(); ready = true;
        }
        catch (Exception ex) { ShowError(ex); }
        Closed += (_, _) => store?.Dispose();
        Render();
    }
    private void ChangeMonth(int direction)
    {
        var next = month.AddMonths(direction);
        if (next.Year is >= 2000 and <= 2099) { month = next; Render(); }
    }
    private void ShowError(Exception ex)
    {
        notice.Text = ex switch
        {
            BudgetException => ex.Message,
            UnauthorizedAccessException => "Sem permissão para acessar o arquivo. Escolha uma pasta gravável. Seus dados não foram substituídos.",
            IOException => "Não foi possível ler ou salvar os dados. Verifique espaço, permissões e se o arquivo está em uso. A operação não foi concluída.",
            _ => "Não foi possível concluir a operação. Seus dados atuais foram preservados."
        };
        notice.IsVisible = true;
    }
    private async Task Run(Func<Task> action)
    {
        try { notice.IsVisible = false; await action(); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void Commit(Action<BudgetData> change)
    {
        if (store is null || !ready) throw new BudgetException("Os dados ainda não estão disponíveis.");
        var next = LocalStore.Clone(data); change(next); store.Save(next); data = next; Render();
    }
    private void Render()
    {
        monthLabel.Text = month.ToString("MMMM 'de' yyyy", Money.Culture);
        for (var i = 0; i < navigation.Count; i++) navigation[i].Classes.Set("active", i == currentPage);
        if (!ready)
        {
            page.Content = Stack(Text("Seus dados precisam de atenção", "title"), Text("Não vamos substituir um arquivo que não pôde ser aberto. Feche outras janelas do aplicativo ou restaure uma cópia válida.", "muted"),
                Button("Tentar abrir novamente", () => _ = Run(() => { store ??= new LocalStore(Environment.GetEnvironmentVariable("BANKMANAGER_DATA_DIR")); data = store.Load(); ready = true; Render(); return Task.CompletedTask; }), "primary"),
                Button("Recuperar cópia automática", () => _ = Run(Recover)), Button("Importar backup", () => _ = Run(Import)));
            return;
        }
        page.Content = currentPage switch { 1 => PeoplePage(), 2 => ExpensesPage(), 3 => ForecastPage(), 4 => SettingsPage(), _ => Dashboard() };
    }
    private static Control Heading(string title, string subtitle) => Stack(Text(title, "title"), Text(subtitle, "muted"));
    private static Control Metric(string label, decimal amount, string hint, string color = "#F0F2F8")
    {
        var value = Text(Money.Format(amount), "money"); value.Foreground = Brush.Parse(color);
        return Card(Stack(Text(label, "muted"), value, Text(hint, "muted")));
    }
    private Control Dashboard()
    {
        var expenses = FinanceEngine.Expenses(data, month); var income = data.People.Sum(p => FinanceEngine.Income(data, p.Id, month)); var total = expenses.Sum(e => e.Amount);
        var result = Stack(Heading("Seu mês, com clareza", "Acompanhe o que entra, o que precisa pagar e quanto sobra."),
            Columns(Metric("Renda da casa", income, $"{data.People.Count} pessoa(s) cadastrada(s)", "#C6B4FF"), Metric("Despesas do mês", total, $"{Money.Format(expenses.Where(e => !e.Paid).Sum(e => e.Amount))} em aberto"), Metric("Saldo planejado", income - total, "Renda menos todas as despesas", income >= total ? "#79DEC0" : "#FFADAD")),
            Row(Button("+  Nova despesa", () => _ = Run(() => EditExpense()), "primary"), Button("Gerenciar pessoas", () => { currentPage = 1; Render(); })));
        if (data.People.Count == 0)
        {
            result.Children.Add(Card(Stack(Text("Vamos organizar suas finanças?", "section"), Text("1. Cadastre quem faz parte da casa e a renda deste mês.\n2. Adicione despesas individuais ou compartilhadas.\n3. Veja a divisão e acompanhe o pagamento.", "muted"), Button("Cadastrar primeira pessoa", () => _ = Run(() => EditPerson()), "primary"))));
            return result;
        }
        var missing = data.People.Where(p => !data.Incomes.Any(i => i.PersonId == p.Id && i.Month == month)).ToList();
        if (missing.Count > 0) result.Children.Add(Card(Text("Renda ainda não informada neste mês: " + string.Join(", ", missing.Select(p => p.Name)) + ". Informe a renda em Pessoas e rendas; até lá, ela será considerada zero.", "muted")));
        result.Children.Add(BalanceCard());
        var categoryPanel = Stack(Text("Para onde vai o dinheiro", "section"));
        if (expenses.Count == 0) categoryPanel.Children.Add(Text("Nenhuma despesa neste mês. Use “Nova despesa” para começar.", "muted"));
        foreach (var g in expenses.GroupBy(e => e.Category).OrderByDescending(g => g.Sum(e => e.Amount)))
        {
            var amount = g.Sum(e => e.Amount);
            categoryPanel.Children.Add(Stack(Text($"{g.Key}  ·  {Money.Format(amount)}"), new ProgressBar { Value = (double)(amount / total * 100), Maximum = 100, Height = 7, Foreground = Brush.Parse("#B6A1FF") }));
        }
        result.Children.Add(Card(categoryPanel));
        var upcoming = expenses.Where(e => !e.Paid).Take(5).ToList();
        if (upcoming.Count > 0)
        {
            result.Children.Add(Text("Próximos pagamentos", "section"));
            foreach (var e in upcoming) result.Children.Add(ExpenseCard(e));
        }
        return result;
    }
    private Control BalanceCard()
    {
        var panel = Stack(Text("Uma divisão que considera cada pessoa", "section"), Text("Disponível para dividir = renda do mês − despesas individuais. Despesas compartilhadas proporcionais usam esse valor como peso.", "muted"));
        foreach (var b in FinanceEngine.Balances(data, month))
        {
            panel.Children.Add(Columns(Stack(Text(b.Person.Name, "section"), Text($"Renda {Money.Format(b.Income)}", "muted")),
                Stack(Text($"Responsabilidade: {Money.Format(b.Assigned)}"), Text($"Quitado: {Money.Format(b.Paid)}", "muted")),
                Stack(Text($"Sobra: {Money.Format(b.Remaining)}"), Text(b.Remaining < 0 ? "Atenção: despesas acima da renda" : "Após todas as despesas do mês", "muted"))));
        }
        foreach (var warning in FinanceEngine.Expenses(data, month).Select(e => FinanceEngine.Allocate(data, e).Warning).Where(w => w is not null).Distinct()) panel.Children.Add(Text(warning!, "muted"));
        return Card(panel);
    }
    private Control PeoplePage()
    {
        var panel = Stack(Heading("Pessoas e rendas", "Informe a renda líquida: o dinheiro que realmente entra no mês selecionado."),
            Row(Button("+  Adicionar pessoa", () => _ = Run(() => EditPerson()), "primary"), Button("Copiar rendas do mês anterior", () => _ = Run(CopyIncome))),
            Text("Cada mês guarda sua própria renda. Alterar o nome vale para todo o histórico; alterar a renda afeta apenas este mês e recalcula sua divisão.", "muted"));
        if (data.People.Count == 0) panel.Children.Add(Card(Text("Adicione você e as pessoas que compartilham as despesas.", "muted")));
        foreach (var person in data.People)
        {
            var income = FinanceEngine.Income(data, person.Id, month);
            panel.Children.Add(Card(Stack(Columns(Stack(Text(person.Name, "section"), Text(data.Incomes.Any(i => i.PersonId == person.Id && i.Month == month) ? "Renda informada neste mês" : "Renda não informada neste mês", "muted")), Text(Money.Format(income), "money")),
                Row(Button("Editar pessoa / renda", () => _ = Run(() => EditPerson(person))), Button("Excluir pessoa", () => _ = Run(async () =>
                {
                    if (data.Expenses.Any(e => e.Participants.Contains(person.Id))) throw new BudgetException("Esta pessoa possui despesas no histórico. Remova ou reatribua essas despesas antes de excluí-la.");
                    if (await Confirm(this, "Excluir pessoa?", $"Excluir {person.Name} e todos os seus registros de renda?")) Commit(d => { d.People.RemoveAll(p => p.Id == person.Id); d.Incomes.RemoveAll(i => i.PersonId == person.Id); });
                }), "danger")))));
        }
        if (data.People.Count > 0) panel.Children.Add(BalanceCard());
        return panel;
    }
    private async Task CopyIncome()
    {
        var previous = data.Incomes.Where(i => i.Month == month.AddMonths(-1) && !data.Incomes.Any(current => current.Month == month && current.PersonId == i.PersonId)).ToList();
        if (previous.Count == 0) throw new BudgetException("Não há rendas anteriores para copiar. Rendas já informadas neste mês não são substituídas.");
        if (await Confirm(this, "Copiar rendas?", $"Preencher {previous.Count} renda(s) ainda não informada(s) com os valores do mês anterior?")) Commit(d => d.Incomes.AddRange(previous.Select(i => i with { Month = month })));
    }
    private Control ExpensesPage()
    {
        var search = Input(filter, "Buscar uma despesa…");
        var status = Combo(["Todos os pagamentos", "Em aberto", "Pagos"], statusFilter);
        var category = Combo(new[] { "Todas as categorias" }.Concat(BudgetRules.Categories), categoryFilter);
        var list = new StackPanel { Spacing = 12 };
        void Refresh()
        {
            list.Children.Clear();
            var entries = FinanceEngine.Expenses(data, month).Where(e => e.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase) && (statusFilter == 0 || e.Paid == (statusFilter == 2)) && (categoryFilter == 0 || e.Category == BudgetRules.Categories[categoryFilter - 1])).ToList();
            list.Children.Add(Text($"{entries.Count} despesa(s)  ·  Total: {Money.Format(entries.Sum(e => e.Amount))}", "muted"));
            foreach (var e in entries) list.Children.Add(ExpenseCard(e));
            if (entries.Count == 0) list.Children.Add(Card(Text("Nenhuma despesa encontrada. Adicione uma despesa ou ajuste os filtros.", "muted")));
        }
        search.TextChanged += (_, _) => { filter = search.Text ?? ""; Refresh(); };
        status.SelectionChanged += (_, _) => { statusFilter = status.SelectedIndex; Refresh(); };
        category.SelectionChanged += (_, _) => { categoryFilter = category.SelectedIndex; Refresh(); };
        Refresh();
        return Stack(Heading("Despesas, sem complicação", "Registre uma conta, uma compra detalhada ou uma dívida parcelada."),
            Row(Button("+  Nova despesa", () => _ = Run(() => EditExpense()), "primary")), Columns(search, status, category), list);
    }
    private Control ExpenseCard(Expense expense)
    {
        var state = expense.Paid ? "Pago" : expense.DueDate < DateOnly.FromDateTime(DateTime.Today) ? "Em atraso" : "Em aberto";
        var suffix = expense.InstallmentCount > 1 ? $" · Parcela {expense.InstallmentNumber}/{expense.InstallmentCount}" : "";
        var allocation = FinanceEngine.Allocate(data, expense);
        var shares = string.Join("  •  ", allocation.Amounts.Select(p => $"{data.People.First(x => x.Id == p.Key).Name}: {Money.Format(p.Value)}"));
        var panel = Stack(Columns(Stack(Text(expense.Title + suffix, "section"), Text($"{expense.Category}  ·  {expense.DueDate:dd/MM/yyyy}  ·  {state}", "muted")), Text(Money.Format(expense.Amount), "money")),
            Text(shares, "muted"), Row(Button(expense.Paid ? "Marcar em aberto" : "Marcar como pago", () => _ = Run(() => { Commit(d => d.Expenses.First(e => e.Id == expense.Id).Paid = !expense.Paid); return Task.CompletedTask; })),
                Button("Detalhes / editar", () => _ = Run(() => EditExpense(expense))), Button("Excluir", () => _ = Run(async () =>
                {
                    if (await Confirm(this, "Excluir despesa?", $"Excluir “{expense.Title}” de {Money.Format(expense.Amount)}? Apenas este lançamento será removido; outras parcelas continuam no histórico.")) Commit(d => d.Expenses.RemoveAll(e => e.Id == expense.Id));
                }), "danger")));
        if (allocation.Warning is not null) panel.Children.Add(Text(allocation.Warning, "muted"));
        return Card(panel);
    }
    private Control ForecastPage()
    {
        var target = month.AddMonths(1); var predictions = FinanceEngine.Forecasts(data, target);
        var panel = Stack(Heading("Olhe para o próximo mês", $"Estimativa para {target.ToString("MMMM 'de' yyyy", Money.Culture)} a partir do histórico até o fim do mês selecionado."),
            Columns(Metric("Estimativa total", predictions.Sum(f => f.Estimate), "Histórico + compromissos conhecidos", "#C6B4FF"), Metric("Já lançado no próximo mês", FinanceEngine.Expenses(data, target).Sum(e => e.Amount), "Inclui parcelas com vencimento no mês")),
            Card(Stack(Text("Como chegamos a esses valores", "section"), Text("Usamos a média de até 3 meses anteriores ao mês previsto. Desde o primeiro mês registrado, meses sem gastos contam como zero. Compras parceladas ficam fora da média: somamos apenas as parcelas que vencem no mês previsto.\n\nEm cada categoria, consideramos o maior valor entre a média e os gastos à vista já lançados; depois somamos as parcelas. Assim, evitamos contar duas vezes um gasto já previsto. São estimativas, não garantias de gastos futuros.", "muted"))));
        if (predictions.Count == 0) panel.Children.Add(Card(Text("Adicione despesas para começar. Um mês com R$ 1.000,00 em mercado gera uma estimativa de R$ 1.000,00 para o próximo mês.", "muted")));
        foreach (var prediction in predictions)
            panel.Children.Add(Card(Columns(Stack(Text(prediction.Category, "section"), Text($"Média histórica: {Money.Format(prediction.Historical)} · {prediction.Months} mês(es) de base\nJá lançado: {Money.Format(prediction.Scheduled)}", "muted")), Stack(Text(Money.Format(prediction.Estimate), "money"), Text(prediction.Months < 3 ? "Pouco histórico: acompanhe de perto" : "Estimativa baseada em 3 meses", "muted")))));
        return panel;
    }
    private Control SettingsPage() => Stack(Heading("Seus dados, sob seu controle", "O aplicativo funciona localmente e não exige cadastro nem conexão com a internet."),
        Card(Stack(Text("Backup e recuperação", "section"), Text("Cada salvamento mantém uma cópia da versão anterior. Exporte backups regularmente para outra pasta ou dispositivo. Os arquivos contêm informações financeiras em texto legível: guarde-os em um local privado.", "muted"),
            Row(Button("Exportar backup", () => _ = Run(Export), "primary"), Button("Importar backup", () => _ = Run(Import))), Button("Recuperar versão anterior", () => _ = Run(Recover)), Text($"Pasta local: {store?.DirectoryPath}", "muted"))),
        Card(Stack(Text("Entenda a divisão", "section"), Text("Individual: uma pessoa paga tudo.\nIgual: o valor é dividido igualmente entre os selecionados.\nProporcional: quem tem mais renda disponível assume uma parcela maior. Despesas individuais, pagas ou em aberto, são descontadas da renda antes da divisão. Todas as despesas proporcionais do mês usam essa mesma base.\n\nExemplo: rendas disponíveis de R$ 3.000 e R$ 1.000 dividem uma conta de R$ 800 em R$ 600 e R$ 200. Quem não tem renda disponível recebe peso zero. Se todos tiverem peso zero, a conta é dividida igualmente, com aviso. Centavos restantes são distribuídos sem alterar o total.", "muted"))),
        Card(Stack(Text("Dívidas, listas e histórico", "section"), Text("Para uma dívida, informe o valor total já com os juros negociados e o número de parcelas. A primeira data é o primeiro vencimento. O aplicativo distribui o total; não calcula taxas bancárias.\n\nUma lista soma quantidade × preço de cada item. Não cadastre a mesma compra novamente como outra despesa. Marcar como pago quita o lançamento inteiro para todos os responsáveis; pagamentos parciais não são registrados nesta versão.\n\nO saldo planejado considera todas as despesas do mês, inclusive as pagas. Alterar rendas ou despesas individuais recalcula o rateio daquele mês. Parcelas são editadas e excluídas individualmente.", "muted"))));
    private async Task Export()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Exportar backup", SuggestedFileName = $"bankmanager-{DateTime.Today:yyyy-MM-dd}.json", DefaultExtension = "json", FileTypeChoices = [new FilePickerFileType("Backup JSON") { Patterns = ["*.json"] }] });
        if (file is null) return;
        var path = file.TryGetLocalPath() ?? throw new BudgetException("Escolha uma pasta local.");
        RejectInternalPath(path); LocalStore.Export(path, data); notice.Text = "Backup exportado com sucesso."; notice.IsVisible = true;
    }
    private void RejectInternalPath(string path)
    {
        if (store is not null && string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(store.DirectoryPath)), StringComparison.OrdinalIgnoreCase))
            throw new BudgetException("Escolha um arquivo fora da pasta interna do aplicativo.");
    }
    private async Task Import()
    {
        if (store is null) throw new BudgetException("Feche outra instância do aplicativo antes de importar dados.");
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Importar backup", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Backup JSON") { Patterns = ["*.json"] }] });
        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath() ?? throw new BudgetException("Escolha um arquivo local.");
        RejectInternalPath(path); var imported = LocalStore.Read(path);
        if (!await Confirm(this, "Substituir os dados atuais?", $"Backup validado: {imported.People.Count} pessoa(s), {imported.Expenses.Count} despesa(s). A importação substitui todo o histórico atual. A versão anterior fica na cópia automática.")) return;
        store.Save(imported); data = imported; ready = true; Render();
    }
    private async Task Recover()
    {
        if (store is null || !File.Exists(store.BackupPath)) throw new BudgetException("Não há cópia automática disponível. Você pode importar um backup exportado anteriormente.");
        var backup = LocalStore.Read(store.BackupPath);
        if (!await Confirm(this, "Recuperar versão anterior?", $"Restaurar {backup.People.Count} pessoa(s) e {backup.Expenses.Count} despesa(s)? Os dados atuais serão substituídos.")) return;
        store.Save(backup); data = backup; ready = true; Render();
    }
    private async Task EditPerson(Person? person = null)
    {
        var name = Input(person?.Name, "Ex.: Ana", 80);
        var amount = Input(person is null ? "" : FinanceEngine.Income(data, person.Id, month).ToString("0.00", Money.Culture), "Ex.: 3000,00", 12);
        var error = Text(""); error.Foreground = Brush.Parse("#FFB4B4");
        var panel = Stack(Heading(person is null ? "Adicionar pessoa" : "Editar pessoa", $"Renda de {month.ToString("MMMM 'de' yyyy", Money.Culture)}"), Field("Nome", name), Field("Renda líquida do mês (R$)", amount, "Use vírgula para centavos. Informe 0 se não houver renda neste mês."), error);
        var dialog = Dialog("Pessoa e renda", panel); dialog.Height = 540;
        panel.Children.Add(Row(Button("Cancelar", () => dialog.Close()), Button("Salvar pessoa", () =>
        {
            try
            {
                var income = Money.Parse(amount.Text, "Renda"); var edited = new Person { Id = person?.Id ?? Guid.NewGuid(), Name = name.Text?.Trim() ?? "" };
                Commit(d =>
                {
                    if (person is null) d.People.Add(edited); else d.People[d.People.FindIndex(p => p.Id == person.Id)] = edited;
                    d.Incomes.RemoveAll(i => i.PersonId == edited.Id && i.Month == month);
                    d.Incomes.Add(new MonthlyIncome { PersonId = edited.Id, Month = month, Amount = income });
                }); dialog.Close();
            }
            catch (Exception ex) { error.Text = FriendlyError(ex); error.BringIntoView(); }
        }, "primary")));
        await dialog.ShowDialog(this);
    }
    private static string FriendlyError(Exception ex) => ex is BudgetException ? ex.Message : ex is IOException or UnauthorizedAccessException ? "Não foi possível salvar. Verifique espaço em disco, permissões e acesso à pasta. Nenhuma alteração foi aplicada." : "Não foi possível concluir. Revise os campos e tente novamente.";
    private async Task EditExpense(Expense? existing = null)
    {
        if (data.People.Count == 0) { currentPage = 1; Render(); throw new BudgetException("Cadastre pelo menos uma pessoa antes de adicionar despesas."); }
        var title = Input(existing?.Title, "Ex.: Compra do mercado", 120);
        var category = Combo(BudgetRules.Categories, existing is null ? 1 : Array.IndexOf(BudgetRules.Categories, existing.Category));
        var date = Input((existing?.DueDate ?? (month == Dates.Month(DateOnly.FromDateTime(DateTime.Today)) ? DateOnly.FromDateTime(DateTime.Today) : month)).ToString("dd/MM/yyyy"), "dd/mm/aaaa", 10);
        var amount = Input(existing?.Amount.ToString("0.00", Money.Culture), "Ex.: 1000,00", 12);
        var installments = Input((existing?.InstallmentCount ?? 1).ToString(), "1", 3); installments.IsEnabled = existing is null;
        var split = Combo(["Individual · uma pessoa", "Igual · partes iguais", "Proporcional · renda disponível"], (int)(existing?.Split ?? (data.People.Count == 1 ? SplitMode.Individual : SplitMode.AvailableIncome)));
        var paid = new CheckBox { Content = "Esta despesa já foi paga", IsChecked = existing?.Paid ?? false };
        var participants = data.People.Select((p, i) => (Person: p, Check: new CheckBox { Content = p.Name, IsChecked = existing?.Participants.Contains(p.Id) ?? true })).ToList();
        // Individual mode behaves like a radio choice while shared modes permit multiple people.
        foreach (var participant in participants)
            participant.Check.IsCheckedChanged += (_, _) =>
            {
                if (split.SelectedIndex == 0 && participant.Check.IsChecked == true)
                    foreach (var other in participants.Where(p => p.Person.Id != participant.Person.Id)) other.Check.IsChecked = false;
            };
        split.SelectionChanged += (_, _) =>
        {
            if (split.SelectedIndex == 0)
            {
                var first = participants.FirstOrDefault(p => p.Check.IsChecked == true).Check ?? participants[0].Check;
                foreach (var p in participants) p.Check.IsChecked = p.Check == first;
            }
        };
        var peoplePanel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var p in participants) { p.Check.Margin = new Thickness(0, 0, 20, 6); peoplePanel.Children.Add(p.Check); }
        var itemList = new StackPanel { Spacing = 8 };
        var items = existing?.Items.Select(i => i with { }).ToList() ?? [];
        var itemName = Input(null, "Nome do item", 100); var quantity = Input("1", "Quantidade", 10); var price = Input(null, "Preço (R$)", 12);
        var itemTotal = Text("", "muted");
        var error = Text(""); error.Foreground = Brush.Parse("#FFB4B4");
        var preview = Text("", "muted");
        void RefreshItems()
        {
            itemList.Children.Clear();
            foreach (var item in items)
            {
                var line = new DockPanel(); var remove = Button("Remover", () => { items.Remove(item); RefreshItems(); }); DockPanel.SetDock(remove, Dock.Right); line.Children.Add(remove);
                line.Children.Add(Text($"{item.Name} · {item.Quantity.ToString(Money.Culture)} × {Money.Format(item.UnitPrice)} = {Money.Format(item.Total)}")); itemList.Children.Add(line);
            }
            amount.IsEnabled = items.Count == 0;
            if (items.Count > 0) amount.Text = items.Sum(i => i.Total).ToString("0.00", Money.Culture);
            itemTotal.Text = items.Count == 0 ? "Sem itens: informe o valor total diretamente." : $"{items.Count} item(ns) · Total: {Money.Format(items.Sum(i => i.Total))}";
            preview.Text = "";
        }
        var addItem = Button("+ Adicionar item", () =>
        {
            try
            {
                BudgetRules.Text(itemName.Text?.Trim(), "Nome do item", 100);
                if (!System.Text.RegularExpressions.Regex.IsMatch(quantity.Text ?? "", @"^\d{1,6}(,\d{1,3})?$") || !decimal.TryParse(quantity.Text, NumberStyles.AllowDecimalPoint, Money.Culture, out var units) || units <= 0 || units > 100_000) throw new BudgetException("Quantidade: use um número entre 0,001 e 100.000, com vírgula para frações.");
                if (items.Count >= 500) throw new BudgetException("A lista permite até 500 itens.");
                var item = new ExpenseItem { Name = itemName.Text!.Trim(), Quantity = units, UnitPrice = Money.Parse(price.Text, "Preço", true) };
                Money.Validate(item.Total, "Total do item", true); Money.Validate(items.Sum(i => i.Total) + item.Total, "Total da lista", true);
                items.Add(item); itemName.Text = ""; quantity.Text = "1"; price.Text = ""; error.Text = ""; RefreshItems();
            }
            catch (Exception ex) { error.Text = FriendlyError(ex); error.BringIntoView(); }
        });
        var itemPanel = Stack(Text("Opcional: detalhe cada item da compra", "muted"), itemList,
            Field("Produto ou serviço", itemName), Columns(Field("Quantidade", quantity), Field("Preço unitário (R$)", price)), addItem, itemTotal);
        var expander = new Expander { Header = "Lista de itens da compra", Content = itemPanel, IsExpanded = items.Count > 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (existing?.InstallmentCount > 1) expander.IsEnabled = false;
        RefreshItems();
        List<Expense> BuildEntries()
        {
            if (!string.IsNullOrWhiteSpace(itemName.Text) || !string.IsNullOrWhiteSpace(price.Text)) throw new BudgetException("Há um item não adicionado. Clique em “Adicionar item” ou limpe seus campos antes de salvar.");
            if (!DateOnly.TryParseExact(date.Text, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var due)) throw new BudgetException("Vencimento: informe uma data válida no formato dd/mm/aaaa.");
            BudgetRules.Date(due);
            if (!int.TryParse(installments.Text, out var count) || count is < 1 or > 120) throw new BudgetException("Use de 1 a 120 parcelas.");
            if (count > 1 && paid.IsChecked == true && existing is null) throw new BudgetException("Crie as parcelas em aberto e marque cada parcela paga na lista de despesas.");
            var entry = new Expense
            {
                Id = existing?.Id ?? Guid.NewGuid(), Title = title.Text?.Trim() ?? "", Category = BudgetRules.Categories[category.SelectedIndex], DueDate = due,
                Amount = items.Count > 0 ? items.Sum(i => i.Total) : Money.Parse(amount.Text, "Valor", true), Paid = paid.IsChecked == true,
                Split = (SplitMode)split.SelectedIndex, Participants = participants.Where(p => p.Check.IsChecked == true).Select(p => p.Person.Id).ToList(), Items = [..items],
                InstallmentGroup = existing?.InstallmentGroup, InstallmentCount = existing?.InstallmentCount ?? 1, InstallmentNumber = existing?.InstallmentNumber ?? 1
            };
            var entries = existing is null ? BudgetRules.Installments(entry, count) : [entry];
            var candidate = LocalStore.Clone(data); if (existing is not null) candidate.Expenses.RemoveAll(e => e.Id == existing.Id); candidate.Expenses.AddRange(entries); BudgetRules.Validate(candidate);
            return entries;
        }
        var panel = Stack(Heading(existing is null ? "Nova despesa" : "Editar despesa", existing?.InstallmentCount > 1 ? $"Alteração apenas da parcela {existing.InstallmentNumber}/{existing.InstallmentCount}." : "Preencha os dados e escolha como dividir."),
            Field("Descrição", title), Columns(Field("Categoria", category), Field("Vencimento (dd/mm/aaaa)", date)),
            Columns(Field(existing?.InstallmentCount > 1 ? "Valor desta parcela (R$)" : "Valor total da compra / dívida (R$)", amount), Field("Número de parcelas", installments)),
            Text("Em dívidas, inclua os juros negociados no valor total. Listas de itens são lançadas à vista.", "muted"), expander,
            Field("Como esta despesa será paga?", split), Text("Selecione os responsáveis (apenas um para despesas individuais):", "muted"), peoplePanel, paid,
            Text("A divisão considera a renda do mês do vencimento. Editar uma despesa individual também recalcula as despesas proporcionais desse mês.", "muted"),
            Button("Conferir divisão antes de salvar", () =>
            {
                try
                {
                    var entries = BuildEntries(); var candidate = LocalStore.Clone(data); if (existing is not null) candidate.Expenses.RemoveAll(e => e.Id == existing.Id); candidate.Expenses.AddRange(entries);
                    var allocation = FinanceEngine.Allocate(candidate, entries[0]);
                    preview.Text = (entries.Count > 1 ? $"Primeira de {entries.Count} parcelas: {Money.Format(entries[0].Amount)}\n" : "") + string.Join("\n", allocation.Amounts.Select(a => $"{data.People.First(p => p.Id == a.Key).Name}: {Money.Format(a.Value)}")) + (allocation.Warning is null ? "" : "\n" + allocation.Warning); error.Text = "";
                }
                catch (Exception ex) { error.Text = FriendlyError(ex); preview.Text = ""; error.BringIntoView(); }
            }), preview, error);
        var dialog = Dialog("Despesa", panel, 760); dialog.Height = 800;
        panel.Children.Add(Row(Button("Cancelar", () => dialog.Close()), Button("Salvar despesa", () =>
        {
            try
            {
                var entries = BuildEntries();
                Commit(d => { if (existing is not null) d.Expenses.RemoveAll(e => e.Id == existing.Id); d.Expenses.AddRange(entries); }); dialog.Close();
            }
            catch (Exception ex) { error.Text = FriendlyError(ex); error.BringIntoView(); }
        }, "primary")));
        await dialog.ShowDialog(this);
    }
}
