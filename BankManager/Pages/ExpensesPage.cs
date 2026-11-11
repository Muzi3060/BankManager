using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Página "Despesas": lista em linhas com filtros e menu de ações, mais o editor de despesas (compra, itens e parcelas).
public sealed partial class MainWindow
{
    // Larguras das colunas da lista: descrição (flexível), vencimento, divisão, situação, valor e menu "⋯".
    private const string ExpenseColumns = "*,72,170,140,120,48";
    /// <summary>
    /// Monta o cabeçalho, a busca e os filtros; ao mudar um filtro, só as linhas são reconstruídas (o campo de busca mantém o foco).
    /// </summary>
    private Control ExpensesPage()
    {
        var search = Input(filter, "Buscar uma despesa…"); search.InnerLeftContent = Icon("Search", 16); search.Padding = new Thickness(12, 10, 12, 10);
        AutomationProperties.SetName(search, "Buscar despesas");
        var category = Combo(new[] { "Todas as categorias" }.Concat(BudgetRules.Categories), categoryFilter); category.MinWidth = 210; category.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(category, "Categoria");
        var summary = Text("", "muted"); var table = new Border(); table.Classes.Add("card"); table.Padding = new Thickness(0);
        // Função local: captura os filtros e refaz resumo e linhas, sem recriar a página inteira.
        void Refresh()
        {
            var entries = FinanceEngine.Expenses(data, month).Where(e => e.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase) && (statusFilter == 0 || e.Paid == (statusFilter == 2)) && (categoryFilter == 0 || e.Category == BudgetRules.Categories[categoryFilter - 1])).ToList();
            summary.Text = $"{entries.Count} {(entries.Count == 1 ? "despesa" : "despesas")} · Total {Money.Format(entries.Sum(e => e.Amount))} · Em aberto {Money.Format(entries.Where(e => !e.Paid).Sum(e => e.Amount))}";
            table.Child = entries.Count == 0 ? EmptyExpenses() : ExpenseTable(entries);
        }
        search.TextChanged += (_, _) => { filter = search.Text ?? ""; Refresh(); };
        category.SelectionChanged += (_, _) => { categoryFilter = category.SelectedIndex; Refresh(); };
        var status = Segmented(["Todas", "Em aberto", "Pagas"], statusFilter, i => { statusFilter = i; Refresh(); });
        var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,Auto,12,Auto") }; Grid.SetColumn(status, 2); Grid.SetColumn(category, 4); filters.Children.Add(search); filters.Children.Add(status); filters.Children.Add(category);
        Refresh();
        var page = Stack(PageHeader("Despesas", "Contas, compras e parcelas do mês.", newExpense: true), filters, summary, table); page.Spacing = 16; return page;
    }
    /// <summary>
    /// Estado vazio amigável: explica por que a lista está vazia e o que fazer em seguida.
    /// </summary>
    private Control EmptyExpenses()
    {
        var any = FinanceEngine.Expenses(data, month).Count > 0;
        var panel = Stack(Text(any ? "Nenhuma despesa encontrada" : "Nenhuma despesa neste mês", "section"), Text(any ? "Tente outra busca ou mude os filtros." : "Registre uma conta, uma compra ou uma dívida parcelada para começar.", "muted"));
        panel.Spacing = 8; panel.HorizontalAlignment = HorizontalAlignment.Center; panel.Margin = new Thickness(24, 40);
        foreach (var child in panel.Children.OfType<TextBlock>()) child.TextAlignment = TextAlignment.Center;
        if (any) { var clear = Button("Limpar filtros", () => { filter = ""; statusFilter = 0; categoryFilter = 0; Render(); }, "link"); clear.HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(clear); }
        else { var add = Button("Nova despesa", () => _ = Run(() => EditExpense()), "primary", "Plus"); add.HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(add); }
        return panel;
    }
    /// <summary>
    /// Tabela de despesas em linhas: descrição, vencimento, divisão por pessoa, situação, valor e menu de ações.
    /// </summary>
    private Control ExpenseTable(IReadOnlyList<Expense> entries)
    {
        var rows = new StackPanel();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions(ExpenseColumns), Margin = new Thickness(20, 12) };
        string[] titles = ["Descrição", "Vence", "Divisão", "Situação", "Valor", ""];
        for (var i = 0; i < titles.Length; i++) { var label = Text(titles[i], "muted"); label.FontSize = 12; label.FontWeight = FontWeight.Medium; if (i == 4) label.TextAlignment = TextAlignment.Right; Grid.SetColumn(label, i); header.Children.Add(label); }
        rows.Children.Add(header);
        foreach (var e in entries) { rows.Children.Add(Divider()); rows.Children.Add(ExpenseRow(e)); }
        return rows;
    }
    /// <summary>
    /// Uma linha da lista. As ações ficam no menu "⋯" para manter a linha limpa.
    /// </summary>
    private Control ExpenseRow(Expense expense)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(ExpenseColumns), Margin = new Thickness(20, 12) };
        var title = Stack(new TextBlock { Text = expense.Title, FontWeight = FontWeight.Medium, TextTrimming = TextTrimming.CharacterEllipsis }, Text(ExpenseSubtitle(expense, false), "muted")); title.Spacing = 0; title.VerticalAlignment = VerticalAlignment.Center; title.Margin = new Thickness(0, 0, 12, 0);
        var due = Text($"{expense.DueDate:dd/MM}", "num"); due.VerticalAlignment = VerticalAlignment.Center;
        // Divisão sem o símbolo R$ (a coluna Valor já mostra a moeda), um nome por linha, números alinhados.
        var shares = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var share in FinanceEngine.Allocate(data, expense).Amounts) { var line = Text($"{data.People.First(p => p.Id == share.Key).Name} {share.Value.ToString("N2", Money.Culture)}", "muted"); line.FontSize = 12; line.Classes.Add("num"); shares.Children.Add(line); }
        var chip = StatusChip(expense);
        var amount = Text(Money.Format(expense.Amount), "num"); amount.FontWeight = FontWeight.SemiBold; amount.TextAlignment = TextAlignment.Right; amount.VerticalAlignment = VerticalAlignment.Center;
        var more = Button("Mais ações", () => { }, "ghost", "More", iconOnly: true); more.Flyout = ExpenseMenu(expense); more.HorizontalAlignment = HorizontalAlignment.Right;
        Control[] cells = [title, due, shares, chip, amount, more];
        for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); grid.Children.Add(cells[i]); }
        return grid;
    }
    /// <summary>
    /// Menu "⋯" de uma despesa: marcar como paga (ou em aberto), editar e excluir.
    /// </summary>
    private MenuFlyout ExpenseMenu(Expense expense)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        menu.Items.Add(MenuAction(expense.Paid ? "Marcar em aberto" : "Marcar como pago", "Check", () => _ = Run(() => { TogglePaid(expense); return Task.CompletedTask; })));
        menu.Items.Add(MenuAction("Editar", "Edit", () => _ = Run(() => EditExpense(expense))));
        menu.Items.Add(MenuAction("Excluir", "Trash", () => _ = Run(async () =>
        {
            if (await Confirm(this, "Excluir despesa?", $"Excluir “{expense.Title}” de {Money.Format(expense.Amount)}? Apenas este lançamento será removido; outras parcelas continuam no histórico.")) Commit(d => d.Expenses.RemoveAll(e => e.Id == expense.Id));
        }), danger: true));
        return menu;
    }
    /// <summary>
    /// Item de menu com ícone. O nome de automação guarda o rótulo (usado por leitores de tela e testes); o item perigoso usa o tom negativo.
    /// </summary>
    private static MenuItem MenuAction(string text, string icon, Action action, bool danger = false)
    {
        var item = new MenuItem { Header = danger ? new TextBlock { Text = text, Classes = { "negative" } } : text, Icon = Icon(icon, 16) };
        if (danger) item.Classes.Add("danger");
        AutomationProperties.SetName(item, text); item.Click += (_, _) => action(); return item;
    }
    /// <summary>
    /// Alterna a situação de pagamento de uma despesa e salva.
    /// </summary>
    private void TogglePaid(Expense expense) => Commit(d => d.Expenses.First(e => e.Id == expense.Id).Paid = !expense.Paid);
    /// <summary>
    /// Linha de apoio sob o título: categoria, parcela ("parcela 3 de 10") e, se pedido, o vencimento.
    /// </summary>
    private static string ExpenseSubtitle(Expense expense, bool withDue) =>
        expense.Category + (expense.InstallmentCount > 1 ? $" · parcela {expense.InstallmentNumber} de {expense.InstallmentCount}" : "") + (withDue ? $" · vence {expense.DueDate:dd/MM}" : "");
    /// <summary>
    /// Chip de situação da lista: Pago, Em aberto, Vence em N dias ou Em atraso.
    /// </summary>
    private static Border StatusChip(Expense expense)
    {
        var s = DueStatus.Of(expense, DateOnly.FromDateTime(DateTime.Today));
        return s.Kind switch
        {
            DueKind.Paid => Chip("Pago", "positive"),
            DueKind.Overdue => Chip("Em atraso", "negative"),
            DueKind.DueSoon => Chip(s.Days == 0 ? "Vence hoje" : $"Vence em {s.Days} {(s.Days == 1 ? "dia" : "dias")}", "warn"),
            _ => Chip("Em aberto")
        };
    }
    /// <summary>
    /// Chip de prazo dos próximos pagamentos: "Em 1 dia", "Em 7 dias", "Hoje" ou "Atrasada há N dias".
    /// </summary>
    private static Border UpcomingChip(Expense expense)
    {
        var s = DueStatus.Of(expense, DateOnly.FromDateTime(DateTime.Today)); var days = Math.Abs(s.Days); var unit = days == 1 ? "dia" : "dias";
        return s.Kind switch
        {
            DueKind.Overdue => Chip($"Atrasada há {days} {unit}", "negative"),
            DueKind.DueSoon => Chip(s.Days == 0 ? "Hoje" : $"Em {days} {unit}", "warn"),
            _ => Chip($"Em {days} {unit}")
        };
    }
    /// <summary>
    /// Abre o editor com um rascunho local, itens e responsáveis; prévia e salvamento compartilham a mesma validação.
    /// </summary>
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
        // No modo individual, as caixas funcionam como uma escolha exclusiva; nos demais, permitem várias pessoas.
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
        // Copia os itens do lançamento para que remover/adicionar no formulário não altere o histórico ao cancelar.
        var items = existing?.Items.Select(i => i with { }).ToList() ?? [];
        var itemName = Input(null, "Nome do item", 100); var quantity = Input("1", "Quantidade", 10); var price = Input(null, "Preço (R$)", 12);
        var itemTotal = Text("", "muted");
        var error = Text("", "error"); 
        var preview = Text("", "muted");
        // Reconstrói itens e total do rascunho. Com itens, o valor manual fica bloqueado; a prévia antiga é descartada.
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
        // O item só entra na lista após validação de quantidade, preço e total acumulado.
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
        // Converte os campos em lançamentos candidatos, valida o conjunto completo e não grava nada em disco.
        List<Expense> BuildEntries()
        {
            // Evita perder um item digitado que ainda não foi confirmado pelo botão Adicionar item.
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
            // Cadastro pode expandir uma compra em parcelas; edição preserva a identidade de um lançamento existente.
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
                    // A prévia usa uma cópia já contendo a edição, pois despesas individuais alteram a base do rateio.
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
                // Repete a validação no salvamento: a prévia não substitui a conferência dos campos atuais.
                var entries = BuildEntries();
                Commit(d => { if (existing is not null) d.Expenses.RemoveAll(e => e.Id == existing.Id); d.Expenses.AddRange(entries); }); dialog.Close();
            }
            catch (Exception ex) { error.Text = FriendlyError(ex); error.BringIntoView(); }
        }, "primary")));
        await dialog.ShowDialog(this);
    }
}
