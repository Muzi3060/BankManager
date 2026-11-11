using Avalonia;
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
        result.Children.Add(columns); result.Children.Add(UpcomingCard(expenses));
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
    /// "Para onde vai o dinheiro": categorias com maior gasto, barra fina e percentual. Muitas categorias pequenas viram "Outras (n)".
    /// </summary>
    private static Control CategoriesPanel(IReadOnlyList<Expense> expenses, decimal total)
    {
        var panel = Stack(Text("Para onde vai o dinheiro", "section")); panel.Spacing = 12;
        if (expenses.Count == 0) { panel.Children.Add(Text("Nenhuma despesa neste mês. Use “Nova despesa” para começar.", "muted")); return panel; }
        var groups = expenses.GroupBy(e => e.Category).Select(g => (Name: g.Key, Amount: g.Sum(e => e.Amount))).OrderByDescending(g => g.Amount).ToList();
        // Com 7 ou mais categorias, mostra as 5 maiores e junta o resto, mantendo a lista curta e legível.
        if (groups.Count >= 7) { var rest = groups.Skip(5).ToList(); groups = [.. groups.Take(5), ($"Outras ({rest.Count})", rest.Sum(g => g.Amount))]; }
        foreach (var (name, amount) in groups)
        {
            var share = total == 0 ? 0 : (double)(amount / total * 100);
            var values = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            values.Children.Add(Text($"{share:0}% ·", "muted")); values.Children.Add(new TextBlock { Text = Money.Format(amount), FontWeight = FontWeight.Medium, Classes = { "num" } });
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; Grid.SetColumn(values, 1); line.Children.Add(new TextBlock { Text = name }); line.Children.Add(values);
            // A cor da barra vem do estilo ProgressBar (token de destaque). Fina e sem rótulo, como no desenho.
            var bar = Stack(line, new ProgressBar { Value = share, Maximum = 100, Height = 5 }); bar.Spacing = 6; panel.Children.Add(bar);
        }
        return panel;
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
            var pay = Button("Marcar como pago", () => _ = Run(() => { TogglePaid(e); return Task.CompletedTask; }));
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), Margin = new Thickness(0, 10) };
            Grid.SetColumn(chip, 1); Grid.SetColumn(amount, 2); Grid.SetColumn(pay, 3); row.Children.Add(title); row.Children.Add(chip); row.Children.Add(amount); row.Children.Add(pay);
            panel.Children.Add(row);
        }
        return Card(panel);
    }
}
