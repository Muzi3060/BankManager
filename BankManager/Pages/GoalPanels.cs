using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Metas por categoria: o cartão de meta (usado na visão geral e em Previsões e metas) e o diálogo que define os valores.
public sealed partial class MainWindow
{
    /// <summary>
    /// Cartão branco de uma meta: valor gasto, "Categoria · meta R$ X", barra, percentual usado à esquerda e, à direita,
    /// o percentual restante ou quanto passou. Embaixo, chips com as despesas da categoria no mês.
    /// A barra é verde até 79%, âmbar de 80% a 100% e vermelha acima de 100%.
    /// </summary>
    /// <param name="wide">Versão em lista (página de metas): acrescenta, à direita do valor, quanto ainda resta em reais.</param>
    private Border GoalCard(GoalProgress goal, IReadOnlyList<Expense> expenses, bool wide = false)
    {
        // Arredonda para baixo: "79%" nunca aparece em âmbar e "100%" só fica vermelho quando de fato passou.
        var used = (int)Math.Floor(goal.Ratio * 100); var tone = goal.Level switch { GoalLevel.Over => "negative", GoalLevel.Near => "warn", _ => "positive" };
        var bar = new ProgressBar { Value = (double)Math.Min(100m, goal.Ratio * 100), Maximum = 100 }; if (goal.Level == GoalLevel.Over) bar.Classes.Add("over"); else if (goal.Level == GoalLevel.Near) bar.Classes.Add("warn");
        var usedLabel = Text(goal.Level == GoalLevel.Over ? $"{used}% da meta" : $"{used}% usado", tone); usedLabel.Classes.Add("small"); usedLabel.Classes.Add("strong");
        // Na lista, o valor que passou já aparece no topo do cartão; aqui fica o percentual.
        var rest = Text(goal.Level != GoalLevel.Over ? $"{100 - used}% restante" : wide ? $"{used - 100}% acima" : $"Acima em {Money.Format(goal.Spent - goal.Goal)}", goal.Level == GoalLevel.Over ? "negative" : "muted"); rest.Classes.Add("small"); rest.TextAlignment = TextAlignment.Right;
        var labels = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") }; Grid.SetColumn(rest, 1); labels.Children.Add(usedLabel); labels.Children.Add(rest);
        var chips = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, -6) };
        var inCategory = expenses.Where(e => e.Category == goal.Category).OrderByDescending(e => e.Amount).ToList();
        // Até três despesas, as maiores primeiro; o restante vira "+N".
        foreach (var e in inCategory.Take(3)) { var chip = Chip(e.Title); ((TextBlock)chip.Child!).TextTrimming = TextTrimming.CharacterEllipsis; chip.MaxWidth = 150; chip.Margin = new Thickness(0, 0, 6, 6); chips.Children.Add(chip); }
        if (inCategory.Count > 3) { var extra = Chip($"+{inCategory.Count - 3}", "status"); extra.Margin = new Thickness(0, 0, 6, 6); chips.Children.Add(extra); }
        if (inCategory.Count == 0) { var none = Chip("Sem despesas no mês"); none.Margin = new Thickness(0, 0, 6, 6); chips.Children.Add(none); }
        var head = Stack(Text(Money.Format(goal.Spent), "money"), Text($"{goal.Category} · meta {Money.Format(goal.Goal)}", "support").WithClass("small")); head.Spacing = 2;
        // Menu "⋯" do cartão: editar as metas ou abrir as despesas da categoria.
        var more = Button("Ações da meta de " + goal.Category, () => { }, "square", "More", iconOnly: true); more.VerticalAlignment = VerticalAlignment.Top;
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        menu.Items.Add(MenuAction("Editar metas", "Target", () => _ = Run(ManageGoals)));
        menu.Items.Add(MenuAction("Ver despesas de " + goal.Category, "List", () => { filter = ""; statusFilter = 0; categoryFilter = BudgetRules.AllCategories(data).IndexOf(goal.Category) + 1; GoTo(2); }));
        more.Flyout = menu; Grid.SetColumn(more, 1);
        Control top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { head, more } };
        if (wide)
        {
            // Na lista há espaço para o valor que resta (ou que passou), alinhado à direita.
            var left = Text(Money.Format(Math.Abs(goal.Goal - goal.Spent)), "strong"); left.FontSize = 16; left.TextAlignment = TextAlignment.Right; if (goal.Level == GoalLevel.Over) left.Classes.Add("negative");
            var caption = Text(goal.Level == GoalLevel.Over ? "Acima da meta" : "Restante", "muted").WithClass("small"); caption.TextAlignment = TextAlignment.Right;
            var side = Stack(caption, left); side.Spacing = 2; Grid.SetColumn(side, 2);
            var icon = IconCircle(CategoryIconName(goal.Category)); icon.Margin = new Thickness(0, 0, 14, 0); Grid.SetColumn(head, 1);
            ((Grid)top).Children.Clear(); side.Margin = new Thickness(0, 0, 14, 0); Grid.SetColumn(more, 3);
            top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Children = { icon, head, side, more } };
        }
        var panel = Stack(top, bar, labels, chips); panel.Spacing = 6;
        bar.Margin = new Thickness(0, 6, 0, 0);
        var card = Card(panel, "solid"); AutomationProperties.SetName(card, $"Meta de {goal.Category}: {Money.Format(goal.Spent)} de {Money.Format(goal.Goal)}, {used}% usado"); return card;
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
            var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,190") }; Grid.SetColumn(label, 1); Grid.SetColumn(box, 2); row.Children.Add(IconCircle(CategoryIconName(name))); row.Children.Add(label); row.Children.Add(box); rows.Children.Add(row);
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
}
