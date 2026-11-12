using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using BankManager.Controls;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Página "Visão geral": saldo planejado com gráfico, resumo da casa, metas do mês, próximos pagamentos, acerto de contas e últimas movimentações.
public sealed partial class MainWindow
{
    /// <summary>
    /// Monta a visão geral, de cima para baixo. Sem pessoas cadastradas, mostra um convite com os primeiros passos.
    /// </summary>
    private Control Dashboard()
    {
        var expenses = FinanceEngine.Expenses(data, month); var income = data.People.Sum(p => FinanceEngine.Income(data, p.Id, month)); var total = expenses.Sum(e => e.Amount);
        var open = expenses.Where(e => !e.Paid).Sum(e => e.Amount);
        var result = Stack(PageHeader("Seu mês, com clareza", null)); result.Spacing = 18;
        if (data.People.Count == 0)
        {
            result.Children.Add(Card(Stack(Text("Vamos organizar suas finanças?", "section"), Text("1. Cadastre quem faz parte da casa e a renda deste mês.\n2. Adicione despesas individuais ou compartilhadas.\n3. Veja a divisão e acompanhe o pagamento.", "muted"), Button("Cadastrar primeira pessoa", () => _ = Run(() => EditPerson()), "primary", "Plus"))));
            return result;
        }
        // 1. Saldo planejado com o gráfico (1,7) e o cartão escuro de resumo (1).
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("1.7*,16,*"), MinHeight = 330 };
        var summary = Card(SummaryPanel(income, total, open), "summary"); Grid.SetColumn(summary, 2); top.Children.Add(Card(BalancePanel(income, total))); top.Children.Add(summary);
        result.Children.Add(top);
        // Registro ausente é diferente de renda explicitamente zero; ambos entram como zero no cálculo.
        var missing = data.People.Where(p => !data.Incomes.Any(i => i.PersonId == p.Id && i.Month == month)).ToList();
        if (missing.Count > 0) result.Children.Add(InlineNotice("Renda ainda não informada neste mês: " + string.Join(", ", missing.Select(p => p.Name)) + ". Informe em Pessoas e rendas; até lá, ela será considerada zero."));
        // 2. Metas do mês.
        result.Children.Add(GoalsSection(expenses));
        // 3. Próximos pagamentos (1,5) e acerto de contas (1). O acerto só faz sentido com mais de uma pessoa.
        var upcoming = UpcomingCard(expenses);
        if (data.People.Count >= 2)
        {
            var middle = new Grid { ColumnDefinitions = new ColumnDefinitions("1.5*,16,*") }; var settlement = SettlementCard(); Grid.SetColumn(settlement, 2);
            middle.Children.Add(upcoming); middle.Children.Add(settlement); result.Children.Add(middle);
        }
        else result.Children.Add(upcoming);
        // 4. Últimas movimentações.
        result.Children.Add(MovementsCard());
        return result;
    }
    /// <summary>
    /// Valor grande em reais: o "R$" (e o sinal de menos, se houver) em tamanho menor, e o número em destaque.
    /// </summary>
    private static TextBlock BigMoney(decimal amount, string style)
    {
        var block = new TextBlock(); block.Classes.Add(style);
        block.Inlines!.Add(new Run((amount < 0 ? "− " : "") + "R$ ") { FontSize = 30, FontWeight = FontWeight.Normal });
        block.Inlines.Add(new Run(Math.Abs(amount).ToString("N2", Money.Culture)));
        AutomationProperties.SetName(block, Money.Format(amount)); return block;
    }
    /// <summary>
    /// "Saldo planejado": o número em destaque, a conta que leva a ele e o gráfico das despesas dos últimos seis meses.
    /// </summary>
    private Control BalancePanel(decimal income, decimal total)
    {
        var balance = BigMoney(income - total, "hero"); if (income < total) balance.Classes.Add("negative");
        var totals = FinanceEngine.MonthlyTotals(data, month, 6);
        // Rótulo curto do mês ("nov"); o .NET devolve "nov." em português, então o ponto final é removido.
        var points = totals.Select(t => new BarPoint(Capitalize(t.Month.ToString("MMM", Money.Culture).TrimEnd('.')), t.Total, Money.Format(t.Total))).ToList();
        var chart = new BarChart(points, points.Count - 1) { Margin = new Thickness(0, 6, 0, 0) };
        // Texto alternativo: quem usa leitor de tela recebe os mesmos valores que o gráfico mostra.
        AutomationProperties.SetName(chart, "Despesas dos últimos 6 meses: " + string.Join("; ", totals.Select(t => $"{t.Month.ToString("MMMM 'de' yyyy", Money.Culture)}, {Money.Format(t.Total)}")) + ".");
        ToolTip.SetTip(chart, "Despesas dos últimos 6 meses. Passe o mouse em uma barra para ver o valor.");
        var head = Stack(Text("Saldo planejado", "strong"), balance, Text($"Renda de {Money.Format(income)} menos despesas de {Money.Format(total)}", "muted").WithClass("small")); head.Spacing = 2;
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") }; Grid.SetRow(chart, 1); grid.Children.Add(head); grid.Children.Add(chart); return grid;
    }
    /// <summary>
    /// Primeira letra maiúscula ("nov" vira "Nov"), para os rótulos de mês.
    /// </summary>
    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0], Money.Culture) + text[1..];
    /// <summary>
    /// Cartão escuro "Resumo da casa": no topo o título e o mês; embaixo, as despesas do mês, quanto da renda elas comprometem,
    /// a renda e o valor em aberto.
    /// </summary>
    private Control SummaryPanel(decimal income, decimal total, decimal open)
    {
        var committed = income > 0 ? Math.Round(total / income * 100, MidpointRounding.AwayFromZero) : 0;
        var bar = new ProgressBar { Value = (double)Math.Min(100, committed), Maximum = 100, Height = 6, Margin = new Thickness(0, 8, 0, 2) }; bar.Classes.Add("onsummary");
        var label = Text(income > 0 ? $"{committed:0}% da renda comprometida" : "Renda do mês ainda não informada", "muted"); label.Classes.Add("small");
        var value = Text(Money.Format(total), "kpi"); value.FontSize = 32; value.FontWeight = FontWeight.SemiBold;
        // Rodapé com dois valores lado a lado.
        Control Pair(string name, decimal amount) { var pair = Stack(Text(name, "muted").WithClass("small"), Text(Money.Format(amount), "strong")); pair.Spacing = 2; return pair; }
        var footer = Columns(Pair("Renda", income), Pair("Em aberto", open)); footer.Margin = new Thickness(0, 14, 0, 0);
        var bottom = Stack(Text("Despesas do mês", "muted"), value, bar, label, footer); bottom.Spacing = 2; bottom.VerticalAlignment = VerticalAlignment.Bottom; Grid.SetRow(bottom, 1);
        var monthName = Text(Capitalize(month.ToString("MMMM", Money.Culture)), "muted"); Grid.SetColumn(monthName, 1);
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { Text("Resumo da casa", "strong"), monthName } };
        return new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { top, bottom } };
    }
    /// <summary>
    /// Cabeçalho de seção: título à esquerda e, opcionalmente, um link de ação à direita.
    /// </summary>
    private static Grid SectionHeading(string title, Button? action = null)
    {
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; var label = Text(title, "section"); label.VerticalAlignment = VerticalAlignment.Center; heading.Children.Add(label);
        if (action is not null) { Grid.SetColumn(action, 1); heading.Children.Add(action); }
        return heading;
    }
    /// <summary>
    /// Aviso discreto dentro da página, para situações que pedem atenção sem bloquear nada.
    /// </summary>
    private static Control InlineNotice(string text)
    {
        var box = new Border { Child = Text(text, "warn") }; box.Classes.Add("notice"); return box;
    }
    /// <summary>
    /// "Metas do mês": até três cartões lado a lado, começando pelas metas mais próximas (ou acima) do limite.
    /// Sem metas, um convite para criar a primeira.
    /// </summary>
    private Control GoalsSection(IReadOnlyList<Expense> expenses)
    {
        var goals = GoalEngine.Progress(data, month).OrderByDescending(g => g.Ratio).ToList();
        var section = Stack(SectionHeading("Metas do mês", Button("Ver todas", () => { forecastTab = 0; GoTo(3); }, "link"))); section.Spacing = 8;
        if (goals.Count == 0)
        {
            var create = Button("Criar primeira meta", () => _ = Run(ManageGoals), "ink"); create.HorizontalAlignment = HorizontalAlignment.Left;
            var empty = Stack(Text("Nenhuma meta ainda", "strong"), Text("Defina quanto quer gastar por mês em uma categoria, como Mercado ou Lazer. O BankManager avisa quando você chegar perto do limite.", "muted"), create); empty.Spacing = 8;
            section.Children.Add(Card(empty)); return section;
        }
        // Sempre três colunas: com uma ou duas metas, os cartões mantêm a mesma largura.
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*,16,*") };
        for (var i = 0; i < Math.Min(3, goals.Count); i++) { var card = GoalCard(goals[i], expenses); Grid.SetColumn(card, i * 2); row.Children.Add(card); }
        section.Children.Add(row);
        return section;
    }
    /// <summary>
    /// "Próximos pagamentos": as três contas em aberto mais próximas, com ícone da categoria, prazo, valor e menu de ações.
    /// </summary>
    private Control UpcomingCard(IReadOnlyList<Expense> expenses)
    {
        var panel = Stack(SectionHeading("Próximos pagamentos", Button("Ver todas as despesas", () => GoTo(2), "link"))); panel.Spacing = 0;
        var upcoming = expenses.Where(e => !e.Paid).Take(3).ToList();
        if (upcoming.Count == 0)
        {
            var empty = Stack(Text(expenses.Count == 0 ? "Nenhuma despesa neste mês ainda" : "Tudo em dia!", "strong"), Text(expenses.Count == 0 ? "Use “Nova despesa” para registrar a primeira conta." : "Nenhum pagamento em aberto neste mês.", "muted")); empty.Spacing = 4; empty.Margin = new Thickness(0, 12, 0, 0);
            panel.Children.Add(empty);
        }
        foreach (var e in upcoming)
        {
            panel.Children.Add(Divider());
            var title = Stack(new TextBlock { Text = e.Title, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis }, Text(ExpenseSubtitle(e, true), "muted").WithClass("small")); title.Spacing = 1; title.VerticalAlignment = VerticalAlignment.Center; title.Margin = new Thickness(12, 0);
            var chip = UpcomingChip(e); var amount = Text(Money.Format(e.Amount), "strong"); amount.TextAlignment = TextAlignment.Right; amount.VerticalAlignment = VerticalAlignment.Center; amount.MinWidth = 100; amount.Margin = new Thickness(12, 0, 4, 0);
            var more = Button("Mais ações", () => { }, "ghost round", "More", iconOnly: true); more.Flyout = ExpenseMenu(e);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"), Margin = new Thickness(0, 10) };
            Control[] cells = [IconCircle(CategoryIconName(e.Category)), title, chip, amount, more];
            for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); row.Children.Add(cells[i]); }
            panel.Children.Add(row);
        }
        return Card(panel);
    }
    // Quantas movimentações a visão geral mostra.
    private const int MovementRows = 6;
    /// <summary>
    /// "Últimas movimentações": rendas do mês e despesas já pagas, das mais recentes para as mais antigas. Todas as linhas têm
    /// opacidade total; o link "Ver tudo" indica que a lista continua na página de despesas.
    /// </summary>
    private Control MovementsCard()
    {
        var panel = Stack(SectionHeading("Últimas movimentações", Button("Ver tudo", () => GoTo(2), "link"))); panel.Spacing = 0;
        var movements = FinanceEngine.RecentMovements(data, month, MovementRows);
        if (movements.Count == 0)
        {
            var empty = Stack(Text("Nada por aqui ainda", "strong"), Text("As rendas do mês e as despesas marcadas como pagas aparecem nesta lista.", "muted")); empty.Spacing = 4; empty.Margin = new Thickness(0, 12, 0, 0);
            panel.Children.Add(empty);
        }
        for (var i = 0; i < movements.Count; i++)
        {
            var m = movements[i]; panel.Children.Add(Divider());
            var title = Stack(new TextBlock { Text = m.Title, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis }, Text($"{m.Category} · {m.Date:dd/MM}", "muted").WithClass("small")); title.Spacing = 1; title.VerticalAlignment = VerticalAlignment.Center; title.Margin = new Thickness(12, 0);
            // Entradas em verde com "+", saídas em texto normal com "−" (o sinal de menos tipográfico).
            var amount = Text((m.IsIncome ? "+ " : "− ") + Money.Format(m.Amount), "strong"); if (m.IsIncome) amount.Classes.Add("positive"); amount.VerticalAlignment = VerticalAlignment.Center;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 10) };
            Grid.SetColumn(title, 1); Grid.SetColumn(amount, 2); row.Children.Add(IconCircle(m.IsIncome ? "ArrowDown" : CategoryIconName(m.Category), m.IsIncome)); row.Children.Add(title); row.Children.Add(amount);
            panel.Children.Add(row);
        }
        return Card(panel);
    }
}
