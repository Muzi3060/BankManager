using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Página "Despesas": lista em linhas com filtros e menu de ações, mais o editor de despesas (compra, itens e parcelas).
public sealed partial class MainWindow
{
    // Larguras das colunas da lista: descrição (flexível), vencimento, divisão, situação, valor e menu "⋯".
    private const string ExpenseColumns = "*,84,170,140,120,48";
    // No modo de seleção, a primeira coluna é a caixa de marcação e o menu "⋯" some.
    private const string SelectColumns = "40,*,84,170,140,120";
    // Máximo de linhas desenhadas ao buscar em todos os meses; o resumo continua contando todas as encontradas.
    private const int MaxRows = 200;
    /// <summary>
    /// Monta o cabeçalho, a busca e os filtros; ao mudar um filtro, só as linhas são reconstruídas (o campo de busca mantém o foco).
    /// O botão "Compartilhar" liga o modo de seleção: caixas de marcação na lista, faixa de atalhos e barra flutuante.
    /// </summary>
    private Control ExpensesPage()
    {
        var search = Input(filter, "Buscar uma despesa…"); search.InnerLeftContent = Icon("Search", 16); search.Padding = new Thickness(12, 10, 12, 10);
        AutomationProperties.SetName(search, "Buscar despesas");
        var categories = BudgetRules.AllCategories(data); var category = Combo(new[] { "Todas as categorias" }.Concat(categories), categoryFilter); category.MinWidth = 190; category.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(category, "Categoria");
        var summary = Text("", "muted"); var table = new Border(); table.Classes.Add("card"); table.Padding = new Thickness(0); table.ClipToBounds = true;
        // Despesas visíveis com os filtros atuais; os atalhos de seleção agem só sobre elas.
        List<Expense> visible = [];
        // Função local: captura os filtros e refaz resumo e linhas, sem recriar a página inteira.
        void Refresh()
        {
            var entries = ExpenseSearch.Run(data, allMonths ? null : month, filter, statusFilter == 0 ? null : statusFilter == 2, categoryFilter == 0 ? null : categories[categoryFilter - 1]);
            visible = entries.Take(MaxRows).ToList();
            summary.Text = $"{entries.Count} {(entries.Count == 1 ? "despesa" : "despesas")} · Total {Money.Format(entries.Sum(e => e.Amount))} · Em aberto {Money.Format(entries.Where(e => !e.Paid).Sum(e => e.Amount))}" + (entries.Count > MaxRows ? $" · mostrando as {MaxRows} mais recentes" : "");
            table.Child = entries.Count == 0 ? EmptyExpenses() : ExpenseTable(visible);
        }
        search.TextChanged += (_, _) => { filter = search.Text ?? ""; Refresh(); };
        category.SelectionChanged += (_, _) => { categoryFilter = category.SelectedIndex; Refresh(); };
        var status = Segmented(["Todas", "Em aberto", "Pagas"], statusFilter, i => { statusFilter = i; Refresh(); });
        var share = Button("Compartilhar", () => { if (selecting) StopSelecting(); else StartSelecting(); }, selecting ? "toggle active" : "toggle", "Share");
        ToolTip.SetTip(share, selecting ? "Sair do modo de seleção" : "Escolher despesas para enviar a outra pessoa");
        var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,Auto,12,Auto,12,Auto") }; Grid.SetColumn(status, 2); Grid.SetColumn(category, 4); Grid.SetColumn(share, 6);
        filters.Children.Add(search); filters.Children.Add(status); filters.Children.Add(category); filters.Children.Add(share);
        Refresh();
        // Resumo à esquerda e acesso às despesas que se repetem todo mês à direita.
        var recurring = Button("Recorrentes", () => _ = Run(ManageRecurrences), "link", "Repeat"); summary.VerticalAlignment = VerticalAlignment.Center;
        // Escopo da busca: o mês selecionado ou todos os meses (a navegação de mês some no modo geral).
        var scope = Segmented(["Este mês", "Todos os meses"], allMonths ? 1 : 0, i => { allMonths = i == 1; Render(); });
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; tools.Children.Add(scope); tools.Children.Add(recurring); Grid.SetColumn(tools, 1);
        var summaryRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; summaryRow.Children.Add(summary); summaryRow.Children.Add(tools);
        var page = Stack(PageHeader("Despesas", allMonths ? "Busca em todos os meses, do mais recente ao mais antigo." : "Contas, compras e parcelas do mês.", monthNavigation: !allMonths), filters); page.Spacing = 16;
        if (selecting)
        {
            // Faixa de atalhos. Eles marcam só o que está visível com a busca e os filtros atuais.
            var all = Button("Todas deste mês", () => { foreach (var e in visible.Where(e => Dates.Month(e.DueDate) == month)) selectedIds.Add(e.Id); Refresh(); UpdateSelectionBar(); }, "pillbtn");
            ToolTip.SetTip(all, "Marca as despesas de " + month.ToString("MMMM 'de' yyyy", Money.Culture) + " que estão na lista");
            var byCategory = Button("Uma categoria…", () => { }, "pillbtn"); var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
            foreach (var name in categories)
            {
                var item = new MenuItem { Header = name, Icon = Icon(CategoryIconName(name), 16) }; AutomationProperties.SetName(item, name);
                item.Click += (_, _) => { foreach (var e in visible.Where(e => e.Category == name)) selectedIds.Add(e.Id); Refresh(); UpdateSelectionBar(); }; menu.Items.Add(item);
            }
            byCategory.Flyout = menu; ToolTip.SetTip(byCategory, "Marca as despesas de uma categoria que estão na lista");
            var clear = Button("Limpar seleção", () => { selectedIds.Clear(); Refresh(); UpdateSelectionBar(); }, "pillbtn");
            var shortcuts = Row(all, byCategory, clear); Grid.SetColumn(shortcuts, 1);
            page.Children.Add(new Border { Child = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { Text("Escolha as despesas que quer enviar."), shortcuts } } }.WithClass("selectbanner"));
        }
        page.Children.Add(summaryRow); page.Children.Add(table);
        // Espaço no fim da página para a barra flutuante não cobrir a última linha.
        if (selecting) page.Children.Add(new Border { Height = 64 });
        return page;
    }
    /// <summary>
    /// Liga o modo de seleção, começando sem nada marcado.
    /// </summary>
    private void StartSelecting() { selecting = true; selectedIds.Clear(); Render(); }
    /// <summary>
    /// Sai do modo de seleção e esquece o que estava marcado.
    /// </summary>
    private void StopSelecting() { if (!selecting) return; selecting = false; selectedIds.Clear(); Render(); }
    /// <summary>
    /// Atualiza a barra flutuante: quantas despesas estão marcadas e o total delas. Sem nenhuma, "Compartilhar…" fica desligado.
    /// </summary>
    private void UpdateSelectionBar()
    {
        selectionBar.IsVisible = selecting && ready && currentPage == 2;
        // Despesas excluídas depois de marcadas saem da conta.
        var chosen = data.Expenses.Where(e => selectedIds.Contains(e.Id)).ToList();
        selectionCount.Text = chosen.Count == 1 ? "1 selecionada" : $"{chosen.Count} selecionadas"; selectionTotal.Text = "Total " + Money.Format(chosen.Sum(e => e.Amount));
        if (selectionGo is not null) selectionGo.IsEnabled = chosen.Count > 0;
    }
    /// <summary>
    /// Estado vazio amigável: explica por que a lista está vazia e o que fazer em seguida.
    /// </summary>
    private Control EmptyExpenses()
    {
        var any = allMonths ? data.Expenses.Count > 0 : FinanceEngine.Expenses(data, month).Count > 0;
        var panel = Stack(Text(any ? "Nenhuma despesa encontrada" : "Nenhuma despesa neste mês", "section"), Text(any ? "Tente outra busca ou mude os filtros." : "Registre uma conta, uma compra ou uma dívida parcelada para começar. Se alguém enviou despesas para você, importe o arquivo.", "muted"));
        panel.Spacing = 8; panel.HorizontalAlignment = HorizontalAlignment.Center; panel.Margin = new Thickness(24, 40);
        foreach (var child in panel.Children.OfType<TextBlock>()) child.TextAlignment = TextAlignment.Center;
        if (any)
        {
            var clear = Button("Limpar filtros", () => { filter = ""; statusFilter = 0; categoryFilter = 0; Render(); }, "link"); clear.HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(clear);
            // Quem não achou no mês talvez procure em outro: oferece ampliar a busca em um clique.
            if (!allMonths && filter.Length > 0) { var everywhere = Button("Buscar em todos os meses", () => { allMonths = true; Render(); }, "link"); everywhere.HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(everywhere); }
        }
        else
        {
            // Quem recebeu um arquivo de outra pessoa pode começar por ele: as despesas são adicionadas, nada é substituído.
            var actions = Row(Button("Nova despesa", () => _ = Run(() => EditExpense()), "primary", "Plus")); actions.HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(actions);
        }
        return panel;
    }
    /// <summary>
    /// Tabela de despesas em linhas: descrição, vencimento, divisão por pessoa, situação, valor e menu de ações
    /// (no modo de seleção, uma caixa de marcação no lugar do menu).
    /// </summary>
    private Control ExpenseTable(IReadOnlyList<Expense> entries)
    {
        var rows = new StackPanel();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions(selecting ? SelectColumns : ExpenseColumns), Margin = new Thickness(22, 14, 14, 12) };
        string[] titles = ["Descrição", "Vence", "Divisão", "Situação", "Valor"];
        for (var i = 0; i < titles.Length; i++) { var label = Text(titles[i], "muted"); label.FontSize = 12; label.FontWeight = FontWeight.Medium; if (i == 4) label.TextAlignment = TextAlignment.Right; Grid.SetColumn(label, selecting ? i + 1 : i); header.Children.Add(label); }
        rows.Children.Add(header);
        foreach (var e in entries) { rows.Children.Add(Divider()); rows.Children.Add(ExpenseRow(e)); }
        return rows;
    }
    /// <summary>
    /// Uma linha da lista. As ações ficam no menu "⋯" para manter a linha limpa. No modo de seleção, o menu dá lugar a uma caixa de
    /// marcação: clicar em qualquer ponto da linha (ou a barra de espaço, com o foco na caixa) alterna a marcação.
    /// </summary>
    private Control ExpenseRow(Expense expense)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(selecting ? SelectColumns : ExpenseColumns), Margin = new Thickness(22, 10, 14, 10) };
        var title = Stack(new TextBlock { Text = expense.Title, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis }, Text(ExpenseSubtitle(expense, false), "muted").WithClass("small")); title.Spacing = 1; title.VerticalAlignment = VerticalAlignment.Center; title.Margin = new Thickness(0, 0, 12, 0);
        var due = Text(allMonths ? $"{expense.DueDate:dd/MM/yy}" : $"{expense.DueDate:dd/MM}", "support"); due.VerticalAlignment = VerticalAlignment.Center;
        // Divisão sem o símbolo R$ (a coluna Valor já mostra a moeda), um nome por linha, números alinhados.
        var shares = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var share in FinanceEngine.Allocate(data, expense).Amounts) { var line = Text($"{data.People.First(p => p.Id == share.Key).Name} {share.Value.ToString("N2", Money.Culture)}", "muted"); line.FontSize = 12; shares.Children.Add(line); }
        var chip = StatusChip(expense);
        var amount = Text(Money.Format(expense.Amount), "strong"); amount.TextAlignment = TextAlignment.Right; amount.VerticalAlignment = VerticalAlignment.Center;
        var row = new Border { Child = grid }.WithClass("exrow");
        Control[] cells;
        if (selecting)
        {
            var check = new CheckBox { IsChecked = selectedIds.Contains(expense.Id), VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 }; AutomationProperties.SetName(check, "Selecionar " + expense.Title);
            row.Classes.Add("selectable"); row.Classes.Set("selected", check.IsChecked == true);
            check.IsCheckedChanged += (_, _) =>
            {
                if (check.IsChecked == true) selectedIds.Add(expense.Id); else selectedIds.Remove(expense.Id);
                row.Classes.Set("selected", check.IsChecked == true); UpdateSelectionBar();
            };
            // Clique fora da caixa também alterna; o clique na própria caixa já é tratado por ela.
            row.PointerPressed += (_, e) => { if (e.Source is Visual source && source.FindAncestorOfType<CheckBox>(true) is null) { check.IsChecked = check.IsChecked != true; e.Handled = true; } };
            amount.Margin = new Thickness(0, 0, 8, 0); cells = [check, title, due, shares, chip, amount];
        }
        else
        {
            var more = Button("Mais ações", () => { }, "ghost round", "More", iconOnly: true); more.Flyout = ExpenseMenu(expense); more.HorizontalAlignment = HorizontalAlignment.Right;
            cells = [title, due, shares, chip, amount, more];
        }
        for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); grid.Children.Add(cells[i]); }
        return row;
    }
    /// <summary>
    /// Menu "⋯" de uma despesa: marcar como paga (ou em aberto), editar e excluir.
    /// </summary>
    private MenuFlyout ExpenseMenu(Expense expense)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        menu.Items.Add(MenuAction(expense.Paid ? "Marcar em aberto" : "Marcar como pago", "Check", () => _ = Run(() => TogglePaid(expense))));
        menu.Items.Add(MenuAction("Editar", "Edit", () => _ = Run(() => EditExpense(expense))));
        menu.Items.Add(MenuAction("Excluir", "Trash", () => _ = Run(async () =>
        {
            var detail = expense.RecurrenceId is not null ? "Apenas este mês será removido e não será criado de novo; a despesa continua nos outros meses. Para parar de vez, use Recorrentes." : "Apenas este lançamento será removido; outras parcelas continuam no histórico.";
            // RemoveExpense registra o mês como ignorado quando o lançamento vem de uma recorrência.
            if (await Confirm(this, "Excluir despesa?", $"Excluir “{expense.Title}” de {Money.Format(expense.Amount)}? {detail}")) Commit(d => RecurrenceEngine.RemoveExpense(d, expense.Id));
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
    /// Alterna a situação de pagamento. Ao marcar como paga, pergunta quem pagou (alimenta o acerto de contas) e grava a data do
    /// pagamento (hoje, ou a informada no diálogo); ao reabrir, esquece o pagador e a data.
    /// </summary>
    private async Task TogglePaid(Expense expense)
    {
        if (expense.Paid) { Commit(d => { var e = d.Expenses.First(x => x.Id == expense.Id); e.Paid = false; e.PaidBy = null; e.PaidDate = null; }); return; }
        var (ok, payer, paidOn) = await AskPayer(expense); if (!ok) return;
        Commit(d => { var e = d.Expenses.First(x => x.Id == expense.Id); e.Paid = true; e.PaidBy = payer; e.PaidDate = paidOn; });
    }
    /// <summary>
    /// Lê uma data digitada no formato dd/mm/aaaa e recusa datas inválidas ou fora do intervalo aceito pelo aplicativo.
    /// </summary>
    private static DateOnly ParseDate(string? text, string label)
    {
        if (!DateOnly.TryParseExact(text?.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw new BudgetException($"{label}: informe uma data válida no formato dd/mm/aaaa.");
        BudgetRules.Date(date); return date;
    }
    /// <summary>
    /// Linha de apoio sob o título. Na lista: categoria e parcela ("parcela 3 de 10") ou "mensal". Na forma curta (visão geral):
    /// "Moradia · 12/11" ou "Parcela 3 de 10 · 15/11", com o vencimento.
    /// </summary>
    private static string ExpenseSubtitle(Expense expense, bool withDue) => withDue
        ? (expense.InstallmentCount > 1 ? $"Parcela {expense.InstallmentNumber} de {expense.InstallmentCount}" : expense.Category) + $" · {expense.DueDate:dd/MM}"
        : expense.Category + (expense.InstallmentCount > 1 ? $" · parcela {expense.InstallmentNumber} de {expense.InstallmentCount}" : expense.RecurrenceId is not null ? " · recorrente" : "");
    /// <summary>
    /// Chip de situação da lista: "Pago em dd/MM" (ou só "Pago", quando a data do pagamento não foi informada), Em aberto,
    /// Vence em N dias ou Em atraso.
    /// </summary>
    private Border StatusChip(Expense expense)
    {
        var s = DueStatus.Of(expense, DateOnly.FromDateTime(DateTime.Today));
        return s.Kind switch
        {
            DueKind.Paid => Chip(expense.PaidDate is { } paidOn ? "Pago em " + paidOn.ToString(allMonths ? "dd/MM/yy" : "dd/MM") : "Pago", "positive"),
            DueKind.Overdue => Chip("Em atraso", "negative"),
            DueKind.DueSoon => Chip(s.Days == 0 ? "Vence hoje" : $"Vence em {s.Days} {(s.Days == 1 ? "dia" : "dias")}", "warn"),
            _ => Chip("Em aberto", "status")
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
        var categories = BudgetRules.AllCategories(data); var category = Combo(categories, existing is null ? 1 : categories.IndexOf(existing.Category));
        var date = Input((existing?.DueDate ?? (month == Dates.Month(DateOnly.FromDateTime(DateTime.Today)) ? DateOnly.FromDateTime(DateTime.Today) : month)).ToString("dd/MM/yyyy"), "dd/mm/aaaa", 10);
        var amount = Input(existing?.Amount.ToString("0.00", Money.Culture), "Ex.: 1000,00", 12);
        var installments = Input((existing?.InstallmentCount ?? 1).ToString(), "1", 3); installments.IsEnabled = existing is null;
        var split = Combo(["Individual · uma pessoa", "Igual · partes iguais", "Proporcional · renda disponível"], (int)(existing?.Split ?? (data.People.Count == 1 ? SplitMode.Individual : SplitMode.AvailableIncome)));
        var paid = new CheckBox { Content = "Esta despesa já foi paga", IsChecked = existing?.Paid ?? false };
        // "Quem pagou" só aparece com a despesa marcada como paga. Com uma única pessoa cadastrada, ela é o padrão.
        var payer = Combo(new[] { "Não informar" }.Concat(data.People.Select(p => p.Name)), existing?.PaidBy is { } known ? data.People.FindIndex(p => p.Id == known) + 1 : data.People.Count == 1 ? 1 : 0);
        // Data do pagamento: hoje ao marcar como paga; vazia em despesas pagas antes de essa informação existir (vale o vencimento).
        var paidDate = Input(existing?.PaidDate?.ToString("dd/MM/yyyy"), "dd/mm/aaaa", 10);
        var payerField = Columns(Field("Quem pagou", payer, "Sem informar, a despesa não entra no acerto de contas."), Field("Data do pagamento (dd/mm/aaaa)", paidDate, "Em branco, o vencimento é usado como referência.")); payerField.IsVisible = paid.IsChecked == true;
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
        // Ao marcar como paga uma despesa de uma só pessoa responsável, sugere essa pessoa como quem pagou.
        paid.IsCheckedChanged += (_, _) =>
        {
            payerField.IsVisible = paid.IsChecked == true;
            if (paid.IsChecked == true && string.IsNullOrWhiteSpace(paidDate.Text) && existing?.Paid != true) paidDate.Text = DateTime.Today.ToString("dd/MM/yyyy");
            var chosen = participants.Where(p => p.Check.IsChecked == true).ToList();
            if (paid.IsChecked == true && payer.SelectedIndex == 0 && chosen.Count == 1) payer.SelectedIndex = data.People.FindIndex(p => p.Id == chosen[0].Person.Id) + 1;
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
        // "Repetir todo mês" só existe no cadastro: cria um modelo e os lançamentos dos próximos meses. Não combina com parcelas nem itens.
        var repeat = new CheckBox { Content = "Repetir todo mês", IsChecked = false }; AutomationProperties.SetName(repeat, "Repetir todo mês");
        repeat.IsCheckedChanged += (_, _) =>
        {
            var on = repeat.IsChecked == true; if (on) installments.Text = "1";
            installments.IsEnabled = !on; expander.IsEnabled = !on;
        };
        var repeatPanel = existing is null ? Stack(repeat, Text($"Para aluguel, internet, assinaturas e contas fixas. Cria a despesa em cada mês, a partir do vencimento, até {RecurrenceEngine.HorizonMonths} meses à frente. Para parar depois, use Recorrentes na lista de despesas.", "muted")) : Stack();
        repeatPanel.Spacing = 6;
        RefreshItems();
        // Converte os campos em lançamentos candidatos, valida o conjunto completo e não grava nada em disco.
        (List<Expense> Entries, Recurrence? Recurrence, BudgetData Candidate) BuildEntries()
        {
            // Evita perder um item digitado que ainda não foi confirmado pelo botão Adicionar item.
            if (!string.IsNullOrWhiteSpace(itemName.Text) || !string.IsNullOrWhiteSpace(price.Text)) throw new BudgetException("Há um item não adicionado. Clique em “Adicionar item” ou limpe seus campos antes de salvar.");
            var due = ParseDate(date.Text, "Vencimento");
            // A data do pagamento é opcional, mas, se preenchida, precisa ser uma data válida.
            DateOnly? paidOn = paid.IsChecked == true && !string.IsNullOrWhiteSpace(paidDate.Text) ? ParseDate(paidDate.Text, "Data do pagamento") : null;
            if (!int.TryParse(installments.Text, out var count) || count is < 1 or > 120) throw new BudgetException("Use de 1 a 120 parcelas.");
            if (count > 1 && paid.IsChecked == true && existing is null) throw new BudgetException("Crie as parcelas em aberto e marque cada parcela paga na lista de despesas.");
            var isRepeat = repeat.IsChecked == true && existing is null;
            if (isRepeat && (count > 1 || items.Count > 0)) throw new BudgetException("Despesas que se repetem todo mês não usam parcelas nem lista de itens.");
            if (isRepeat && paid.IsChecked == true) throw new BudgetException("Crie a despesa recorrente em aberto e marque cada mês como pago na lista de despesas.");
            var entry = new Expense
            {
                Id = existing?.Id ?? Guid.NewGuid(), Title = title.Text?.Trim() ?? "", Category = categories[category.SelectedIndex], DueDate = due,
                Amount = items.Count > 0 ? items.Sum(i => i.Total) : Money.Parse(amount.Text, "Valor", true), Paid = paid.IsChecked == true, PaidBy = paid.IsChecked == true && payer.SelectedIndex > 0 ? data.People[payer.SelectedIndex - 1].Id : null, PaidDate = paidOn, OriginId = existing?.OriginId,
                Split = (SplitMode)split.SelectedIndex, Participants = participants.Where(p => p.Check.IsChecked == true).Select(p => p.Person.Id).ToList(), Items = [..items],
                InstallmentGroup = existing?.InstallmentGroup, InstallmentCount = existing?.InstallmentCount ?? 1, InstallmentNumber = existing?.InstallmentNumber ?? 1,
                // Editar um mês de uma recorrência preserva o vínculo (e o mês de origem) para ela não recriá-lo.
                RecurrenceId = existing?.RecurrenceId, RecurrenceMonth = existing?.RecurrenceMonth
            };
            if (isRepeat)
            {
                var recurrence = new Recurrence { Title = entry.Title, Category = entry.Category, Amount = entry.Amount, DayOfMonth = due.Day, Split = entry.Split, Participants = [.. entry.Participants], StartMonth = Dates.Month(due) };
                var plan = LocalStore.Clone(data); plan.Recurrences.Add(recurrence); RecurrenceEngine.Generate(plan, DateOnly.FromDateTime(DateTime.Today)); BudgetRules.Validate(plan);
                return (plan.Expenses.Where(e => e.RecurrenceId == recurrence.Id).OrderBy(e => e.DueDate).ToList(), recurrence, plan);
            }
            // Cadastro pode expandir uma compra em parcelas; edição preserva a identidade de um lançamento existente.
            var entries = existing is null ? BudgetRules.Installments(entry, count) : [entry];
            var candidate = LocalStore.Clone(data); if (existing is not null) candidate.Expenses.RemoveAll(e => e.Id == existing.Id); candidate.Expenses.AddRange(entries); BudgetRules.Validate(candidate);
            return (entries, null, candidate);
        }
        var panel = Stack(Heading(existing is null ? "Nova despesa" : "Editar despesa", existing?.InstallmentCount > 1 ? $"Alteração apenas da parcela {existing.InstallmentNumber}/{existing.InstallmentCount}." : existing?.RecurrenceId is not null ? "Esta despesa se repete todo mês. A alteração vale só para este mês." : "Preencha os dados e escolha como dividir."),
            Field("Descrição", title), Columns(Field("Categoria", category), Field("Vencimento (dd/mm/aaaa)", date)),
            Columns(Field(existing?.InstallmentCount > 1 ? "Valor desta parcela (R$)" : "Valor total da compra / dívida (R$)", amount), Field("Número de parcelas", installments)), repeatPanel,
            Text("Em dívidas, inclua os juros negociados no valor total. Listas de itens são lançadas à vista.", "muted"), expander,
            Field("Como esta despesa será paga?", split), Text("Selecione os responsáveis (apenas um para despesas individuais):", "muted"), peoplePanel, paid, payerField,
            Text("A divisão considera a renda do mês do vencimento. Editar uma despesa individual também recalcula as despesas proporcionais desse mês.", "muted"),
            Button("Conferir divisão antes de salvar", () =>
            {
                try
                {
                    // A prévia usa uma cópia já contendo a edição, pois despesas individuais alteram a base do rateio.
                    var (entries, recurrence, candidate) = BuildEntries();
                    var allocation = FinanceEngine.Allocate(candidate, entries[0]);
                    var heading = recurrence is not null ? $"Repete todo mês no dia {recurrence.DayOfMonth}: {entries.Count} lançamentos, até {entries[^1].RecurrenceMonth:MM/yyyy}. Valor mensal: {Money.Format(entries[0].Amount)}\n" : entries.Count > 1 ? $"Primeira de {entries.Count} parcelas: {Money.Format(entries[0].Amount)}\n" : "";
                    preview.Text = heading + string.Join("\n", allocation.Amounts.Select(a => $"{data.People.First(p => p.Id == a.Key).Name}: {Money.Format(a.Value)}")) + (allocation.Warning is null ? "" : "\n" + allocation.Warning); error.Text = "";
                }
                catch (Exception ex) { error.Text = FriendlyError(ex); preview.Text = ""; error.BringIntoView(); }
            }), preview, error);
        var dialog = Dialog("Despesa", panel, 760); dialog.Height = 800;
        panel.Children.Add(Row(Button("Cancelar", () => dialog.Close()), Button("Salvar despesa", () =>
        {
            try
            {
                // Repete a validação no salvamento: a prévia não substitui a conferência dos campos atuais.
                var (entries, recurrence, _) = BuildEntries();
                Commit(d =>
                {
                    // Recorrência: guarda o modelo e deixa o motor criar os meses; caso contrário, grava os lançamentos montados.
                    if (recurrence is not null) { d.Recurrences.Add(recurrence); RecurrenceEngine.Generate(d, DateOnly.FromDateTime(DateTime.Today)); return; }
                    if (existing is not null) d.Expenses.RemoveAll(e => e.Id == existing.Id); d.Expenses.AddRange(entries);
                }); dialog.Close();
            }
            catch (Exception ex) { error.Text = FriendlyError(ex); error.BringIntoView(); }
        }, "primary")));
        await dialog.ShowDialog(this);
    }
}
