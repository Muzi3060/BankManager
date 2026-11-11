using Avalonia;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Página "Despesas": lista mensal com filtros e o editor de despesas (compra, itens e parcelas).
public sealed partial class MainWindow
{
    /// <summary>
    /// Monta a busca e os filtros da lista mensal; atualiza apenas a lista quando um filtro muda.
    /// </summary>
    private Control ExpensesPage()
    {
        var search = Input(filter, "Buscar uma despesa…");
        var status = Combo(["Todos os pagamentos", "Em aberto", "Pagos"], statusFilter);
        var category = Combo(new[] { "Todas as categorias" }.Concat(BudgetRules.Categories), categoryFilter);
        var list = new StackPanel { Spacing = 12 };
        // Função local captura os filtros e reconstrói apenas os cartões que correspondem à busca.
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
            Row(Button("Nova despesa", () => _ = Run(() => EditExpense()), "primary", "Plus")), Columns(search, status, category), list);
    }
    /// <summary>
    /// Apresenta um lançamento e seu rateio; permite alternar quitação, editar ou excluir apenas esse lançamento.
    /// </summary>
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
