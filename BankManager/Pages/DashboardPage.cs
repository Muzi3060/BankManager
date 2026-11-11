using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Página "Visão geral": indicadores do mês, para onde vai o dinheiro, divisão por pessoa e próximos pagamentos.
public sealed partial class MainWindow
{
    /// <summary>
    /// Monta a visão geral. Sem pessoas cadastradas, mostra um convite com os primeiros passos.
    /// </summary>
    private Control Dashboard()
    {
        var expenses = FinanceEngine.Expenses(data, month); var income = data.People.Sum(p => FinanceEngine.Income(data, p.Id, month)); var total = expenses.Sum(e => e.Amount);
        var open = expenses.Where(e => !e.Paid).Sum(e => e.Amount);
        var result = Stack(PageHeader("Seu mês, com clareza", "O que entra, o que sai e quanto sobra.", newExpense: true));
        result.Spacing = 20;
        if (data.People.Count == 0)
        {
            result.Children.Add(Card(Stack(Text("Vamos organizar suas finanças?", "section"), Text("1. Cadastre quem faz parte da casa e a renda deste mês.\n2. Adicione despesas individuais ou compartilhadas.\n3. Veja a divisão e acompanhe o pagamento.", "muted"), Button("Cadastrar primeira pessoa", () => _ = Run(() => EditPerson()), "primary", "Plus"))));
            return result;
        }
        // Faixa única com três indicadores separados por linhas finas; o saldo muda de cor conforme o sinal.
        var people = data.People.Count == 1 ? "1 pessoa na casa" : $"{data.People.Count} pessoas na casa";
        result.Children.Add(IndicatorStrip(Indicator("Renda da casa", Money.Format(income), people), Indicator("Despesas do mês", Money.Format(total), $"{Money.Format(open)} em aberto"),
            Indicator("Saldo planejado", Money.Format(income - total), "Renda menos todas as despesas", income >= total ? "positive" : "negative")));
        // Registro ausente é diferente de renda explicitamente zero; ambos entram como zero no cálculo.
        var missing = data.People.Where(p => !data.Incomes.Any(i => i.PersonId == p.Id && i.Month == month)).ToList();
        if (missing.Count > 0) result.Children.Add(InlineNotice("Renda ainda não informada neste mês: " + string.Join(", ", missing.Select(p => p.Name)) + ". Informe em Pessoas e rendas; até lá, ela será considerada zero."));
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") };
        var left = Card(CategoriesPanel(expenses, total)); var right = Card(PeoplePanel()); Grid.SetColumn(right, 2); columns.Children.Add(left); columns.Children.Add(right);
        result.Children.Add(columns);
        // Acerto de contas só faz sentido quando há mais de uma pessoa.
        if (data.People.Count >= 2) result.Children.Add(SettlementCard());
        result.Children.Add(UpcomingCard(expenses));
        return result;
    }
    /// <summary>
    /// Um indicador da faixa superior: rótulo, valor grande (com tom opcional) e contexto.
    /// </summary>
    private static Control Indicator(string label, string value, string hint, string? tone = null)
    {
        var number = Text(value, "kpi"); if (tone is not null) number.Classes.Add(tone);
        var panel = Stack(Text(label, "muted"), number, Text(hint, "muted")); panel.Spacing = 4; panel.Margin = new Thickness(24, 20); return panel;
    }
    /// <summary>
    /// Reúne indicadores em um único cartão, com uma linha vertical fina entre eles.
    /// </summary>
    private static Control IndicatorStrip(params Control[] items)
    {
        // Colunas: indicador, e para cada indicador seguinte uma coluna fina de divisória + uma coluna de conteúdo.
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*" + string.Concat(Enumerable.Repeat(",Auto,*", items.Length - 1))) };
        var column = 0;
        for (var i = 0; i < items.Length; i++)
        {
            if (i > 0) { var line = new Border { Width = 1 }; line.Classes.Add("divider"); line.Height = double.NaN; Grid.SetColumn(line, column++); grid.Children.Add(line); }
            Grid.SetColumn(items[i], column++); grid.Children.Add(items[i]);
        }
        var card = Card(grid); card.Padding = new Thickness(0); return card;
    }
    /// <summary>
    /// Aviso discreto dentro da página, para situações que pedem atenção sem bloquear nada.
    /// </summary>
    private static Control InlineNotice(string text)
    {
        var box = new Border { Child = Text(text, "warn") }; box.Classes.Add("notice"); return box;
    }
    /// <summary>
    /// "Para onde vai o dinheiro": categorias com barra fina. Categorias com meta mostram "R$ gasto de R$ meta" e a barra acompanha a meta
    /// (alerta a partir de 80%, vermelha acima de 100%); as demais mostram o percentual do total. Muitas categorias pequenas viram "Outras (n)".
    /// </summary>
    private Control CategoriesPanel(IReadOnlyList<Expense> expenses, decimal total)
    {
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var goalsButton = Button("Metas", () => _ = Run(ManageGoals), "link", "Target"); Grid.SetColumn(goalsButton, 1); heading.Children.Add(Text("Para onde vai o dinheiro", "section")); heading.Children.Add(goalsButton);
        var panel = Stack(heading); panel.Spacing = 12;
        var goals = GoalEngine.Progress(data, month).ToDictionary(g => g.Category);
        if (expenses.Count == 0 && goals.Count == 0) { panel.Children.Add(Text("Nenhuma despesa neste mês. Use “Nova despesa” para começar. Em “Metas” você define um limite por categoria.", "muted")); return panel; }
        var spent = expenses.GroupBy(e => e.Category).Select(g => (Name: g.Key, Amount: g.Sum(e => e.Amount))).ToList();
        // Categorias com meta aparecem sempre (mesmo sem gasto no mês); as outras entram pelo gasto e podem ser agrupadas.
        var rows = goals.Values.Select(g => (Name: g.Category, Amount: g.Spent)).ToList();
        var others = spent.Where(c => !goals.ContainsKey(c.Name)).OrderByDescending(c => c.Amount).ToList();
        var keep = Math.Max(2, 6 - rows.Count);
        if (others.Count > keep + 1) { var rest = others.Skip(keep).ToList(); others = [.. others.Take(keep), ($"Outras ({rest.Count})", rest.Sum(c => c.Amount))]; }
        rows.AddRange(others);
        foreach (var (name, amount) in rows.OrderByDescending(r => !r.Name.StartsWith("Outras (") ? 1 : 0).ThenByDescending(r => r.Amount))
        {
            goals.TryGetValue(name, out var goal);
            var values = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; double share; string[] barClass = [];
            if (goal is null)
            {
                share = total == 0 ? 0 : (double)(amount / total * 100); values.Children.Add(Text($"{share:0}% ·", "muted"));
                values.Children.Add(new TextBlock { Text = Money.Format(amount), FontWeight = FontWeight.Medium, Classes = { "num" } });
            }
            else
            {
                share = (double)Math.Min(100m, goal.Ratio * 100); barClass = goal.Level switch { GoalLevel.Over => ["over"], GoalLevel.Near => ["warn"], _ => [] };
                var used = new TextBlock { Text = Money.Format(amount), FontWeight = FontWeight.Medium, Classes = { "num" } }; if (goal.Level == GoalLevel.Over) used.Classes.Add("negative");
                values.Children.Add(used); values.Children.Add(Text($"de {Money.Format(goal.Goal)}", "muted"));
            }
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; Grid.SetColumn(values, 1); line.Children.Add(new TextBlock { Text = name }); line.Children.Add(values);
            // A cor da barra vem do estilo ProgressBar (token de destaque) e das classes de alerta; fina e sem rótulo.
            var bar = new ProgressBar { Value = share, Maximum = 100, Height = 5 }; foreach (var c in barClass) bar.Classes.Add(c);
            var item = Stack(line, bar); item.Spacing = 6; panel.Children.Add(item);
        }
        return panel;
    }
    /// <summary>
    /// Diálogo de metas: um campo por categoria; vazio significa "sem meta". A meta vale para todos os meses.
    /// </summary>
    private async Task ManageGoals()
    {
        var inputs = BudgetRules.AllCategories(data).Select(c => (Name: c, Box: Input(data.Goals.FirstOrDefault(g => g.Category == c)?.Amount.ToString("0.00", Money.Culture), "Sem meta", 12))).ToList();
        var rows = new StackPanel { Spacing = 8 };
        foreach (var (name, box) in inputs)
        {
            AutomationProperties.SetName(box, "Meta de " + name);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,190") }; var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(box, 1); row.Children.Add(label); row.Children.Add(box); rows.Children.Add(row);
        }
        var error = Text("", "error");
        var panel = Stack(Heading("Metas por categoria", "Defina quanto quer gastar por mês em cada categoria. A meta vale para todos os meses; deixe em branco para não ter meta."), rows, error);
        var dialog = Dialog("Metas por categoria", panel, 620); dialog.Height = 700;
        panel.Children.Add(Row(Button("Cancelar", () => dialog.Close()), Button("Salvar metas", () =>
        {
            try
            {
                // Lê e valida tudo antes de gravar: uma meta com erro não deixa salvar as outras pela metade.
                var parsed = inputs.Select(i => (i.Name, Amount: string.IsNullOrWhiteSpace(i.Box.Text) ? (decimal?)null : Money.Parse(i.Box.Text, "Meta de " + i.Name, true))).ToList();
                Commit(d => { foreach (var (name, amount) in parsed) { if (amount is { } value) GoalEngine.Set(d, name, value); else GoalEngine.Clear(d, name); } }); dialog.Close();
            }
            catch (Exception ex) { error.Text = FriendlyError(ex); }
        }, "primary")));
        await dialog.ShowDialog(this);
    }
    /// <summary>
    /// "Divisão por pessoa": avatar, renda, sobra e quanto da renda está comprometido com as despesas do mês.
    /// </summary>
    private Control PeoplePanel()
    {
        var panel = Stack(Text("Divisão por pessoa", "section")); panel.Spacing = 16;
        foreach (var b in FinanceEngine.Balances(data, month))
        {
            var committed = b.Income > 0 ? (double)Math.Min(100, b.Assigned / b.Income * 100) : 0;
            var remaining = Text(Money.Format(b.Remaining), "num"); remaining.FontWeight = FontWeight.SemiBold; remaining.Classes.Add(b.Remaining < 0 ? "negative" : "positive");
            var label = Text("Sobra", "muted"); label.FontSize = 12;
            var right = Stack(label, remaining); right.Spacing = 0; right.HorizontalAlignment = HorizontalAlignment.Right;
            var who = Stack(new TextBlock { Text = b.Person.Name, FontWeight = FontWeight.Medium }, Text($"Renda {Money.Format(b.Income)}", "muted")); who.Spacing = 0; who.VerticalAlignment = VerticalAlignment.Center;
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") }; var avatar = Avatar(b.Person.Name); avatar.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(who, 1); Grid.SetColumn(right, 2); head.Children.Add(avatar); head.Children.Add(who); head.Children.Add(right);
            var detail = b.Income > 0 ? $"Responsável por {Money.Format(b.Assigned)} · {committed:0}% da renda" : $"Responsável por {Money.Format(b.Assigned)} · renda não informada";
            var person = Stack(head, new ProgressBar { Value = committed, Maximum = 100, Height = 5 }, Text(detail, "muted")); person.Spacing = 8; panel.Children.Add(person);
        }
        panel.Children.Add(Text("Divisão proporcional à renda disponível de cada pessoa.", "muted"));
        foreach (var warning in FinanceEngine.Expenses(data, month).Select(e => FinanceEngine.Allocate(data, e).Warning).Where(w => w is not null).Distinct()) panel.Children.Add(Text(warning!, "warn"));
        return panel;
    }
    /// <summary>
    /// "Próximos pagamentos": as cinco contas em aberto mais próximas, com prazo e ação de marcar como paga.
    /// </summary>
    private Control UpcomingCard(IReadOnlyList<Expense> expenses)
    {
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        var all = Button("Ver todas as despesas", () => GoTo(2), "link"); Grid.SetColumn(all, 1); heading.Children.Add(Text("Próximos pagamentos", "section")); heading.Children.Add(all);
        var panel = Stack(heading); panel.Spacing = 0;
        var upcoming = expenses.Where(e => !e.Paid).Take(5).ToList();
        if (upcoming.Count == 0) panel.Children.Add(Text(expenses.Count == 0 ? "Nenhuma despesa neste mês ainda. Use “Nova despesa” para começar." : "Tudo em dia! Nenhum pagamento em aberto neste mês.", "muted"));
        foreach (var e in upcoming)
        {
            panel.Children.Add(Divider());
            var title = Stack(new TextBlock { Text = e.Title, FontWeight = FontWeight.Medium }, Text(ExpenseSubtitle(e, true), "muted")); title.Spacing = 0; title.VerticalAlignment = VerticalAlignment.Center;
            var chip = UpcomingChip(e); var amount = Text(Money.Format(e.Amount), "num"); amount.FontWeight = FontWeight.SemiBold; amount.TextAlignment = TextAlignment.Right; amount.VerticalAlignment = VerticalAlignment.Center; amount.MinWidth = 110; amount.Margin = new Thickness(16, 0);
            var pay = Button("Marcar como pago", () => _ = Run(() => TogglePaid(e)));
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), Margin = new Thickness(0, 10) };
            Grid.SetColumn(chip, 1); Grid.SetColumn(amount, 2); Grid.SetColumn(pay, 3); row.Children.Add(title); row.Children.Add(chip); row.Children.Add(amount); row.Children.Add(pay);
            panel.Children.Add(row);
        }
        return Card(panel);
    }
}
