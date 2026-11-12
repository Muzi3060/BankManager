using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Página "Previsões e metas": um submenu reúne as metas por categoria, a previsão do próximo mês, as despesas recorrentes e o acerto de contas.
public sealed partial class MainWindow
{
    /// <summary>
    /// Monta a página com o submenu interno. A aba escolhida fica guardada na janela, para sobreviver a um salvamento.
    /// Recorrentes e acerto continuam acessíveis também de onde já estavam (Despesas e Visão geral).
    /// </summary>
    private Control ForecastPage()
    {
        var tabs = Segmented(["Metas por categoria", "Previsão do próximo mês", "Despesas recorrentes", "Acerto de contas"], forecastTab, i => { forecastTab = i; Render(); });
        var panel = Stack(PageHeader("Previsões e metas", "Metas, previsão, contas que se repetem e acerto de contas."), tabs); panel.Spacing = 18;
        foreach (var block in forecastTab switch { 1 => ForecastTab(), 2 => RecurrencesTab(), 3 => SettlementTab(), _ => GoalsTab() }) panel.Children.Add(block);
        return panel;
    }
    /// <summary>
    /// Estado vazio padrão de uma aba: título, explicação e, se houver, uma ação.
    /// </summary>
    private static Border EmptyCard(string title, string body, Button? action = null)
    {
        var empty = Stack(Text(title, "section"), Text(body, "muted")); empty.Spacing = 8; empty.Margin = new Thickness(4, 18);
        if (action is not null) { action.HorizontalAlignment = HorizontalAlignment.Left; empty.Children.Add(action); }
        return Card(empty);
    }
    /// <summary>
    /// Aba de metas: um cartão branco por meta, em lista, com valor gasto, meta, restante, barra e as despesas da categoria.
    /// </summary>
    private IEnumerable<Control> GoalsTab()
    {
        var goals = GoalEngine.Progress(data, month); var expenses = FinanceEngine.Expenses(data, month);
        if (goals.Count == 0) { yield return EmptyCard("Nenhuma meta ainda", "Defina quanto quer gastar por mês em uma categoria, como Mercado ou Lazer. A barra fica verde até 79%, âmbar de 80% a 100% e vermelha quando passa do limite.", Button("Criar primeira meta", () => _ = Run(ManageGoals), "ink")); yield break; }
        var over = goals.Count(g => g.Level == GoalLevel.Over);
        var summary = Text($"{goals.Count} {(goals.Count == 1 ? "meta" : "metas")} em {month.ToString("MMMM 'de' yyyy", Money.Culture)}" + (over > 0 ? $" · {over} acima do limite" : " · todas dentro do limite"), "muted"); summary.VerticalAlignment = VerticalAlignment.Center;
        var manage = Button("Gerenciar metas", () => _ = Run(ManageGoals), "ink", "Target"); Grid.SetColumn(manage, 1);
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; top.Children.Add(summary); top.Children.Add(manage); yield return top;
        foreach (var goal in goals) yield return GoalCard(goal, expenses, wide: true);
        yield return Text("A meta é mensal e vale para todos os meses. Ela soma todas as despesas da categoria no mês, pagas ou em aberto.", "muted");
    }
    /// <summary>
    /// Aba de previsão: estimativa para o mês seguinte ao selecionado, por categoria, com a explicação do cálculo em uma seção que abre e fecha.
    /// </summary>
    private IEnumerable<Control> ForecastTab()
    {
        var target = month.AddMonths(1); var predictions = FinanceEngine.Forecasts(data, target);
        yield return Text($"Estimativa para {target.ToString("MMMM 'de' yyyy", Money.Culture)}, a partir do histórico até o mês selecionado.", "muted");
        // Dois números lado a lado: quanto se espera gastar e quanto já está lançado.
        Control Stat(string label, decimal value, string hint) { var stat = Stack(Text(label, "muted"), Text(Money.Format(value), "kpi"), Text(hint, "muted").WithClass("small")); stat.Spacing = 2; return stat; }
        yield return Card(Columns(Stat("Estimativa total", predictions.Sum(f => f.Estimate), "Histórico + compromissos conhecidos"), Stat("Já lançado no próximo mês", FinanceEngine.Expenses(data, target).Sum(e => e.Amount), "Inclui parcelas com vencimento no mês")));
        if (predictions.Count == 0) yield return EmptyCard("Ainda não há o que prever", "Adicione despesas para começar. Um mês com R$ 1.000,00 em mercado gera uma estimativa de R$ 1.000,00 para o próximo mês.");
        else
        {
            var rows = new StackPanel { Spacing = 0 };
            foreach (var prediction in predictions)
            {
                if (rows.Children.Count > 0) rows.Children.Add(Divider());
                var info = Stack(new TextBlock { Text = prediction.Category, FontWeight = FontWeight.SemiBold, FontSize = 13 }, Text($"Média {Money.Format(prediction.Historical)} · {prediction.Months} {(prediction.Months == 1 ? "mês" : "meses")} de base · já lançado {Money.Format(prediction.Scheduled)}", "muted").WithClass("small")); info.Spacing = 1; info.VerticalAlignment = VerticalAlignment.Center; info.Margin = new Thickness(12, 0);
                var amount = Text(Money.Format(prediction.Estimate), "strong"); amount.VerticalAlignment = VerticalAlignment.Center; amount.TextAlignment = TextAlignment.Right; amount.Width = 120;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(0, 10) }; Grid.SetColumn(info, 1); row.Children.Add(IconCircle(CategoryIconName(prediction.Category))); row.Children.Add(info);
                if (prediction.Months < 3) { var chip = Chip("Pouco histórico", "warn"); Grid.SetColumn(chip, 2); row.Children.Add(chip); }
                Grid.SetColumn(amount, 3); row.Children.Add(amount); rows.Children.Add(row);
            }
            yield return Card(rows);
        }
        var how = Card(Collapsible("Como chegamos a esses valores", "Usamos a média de até 3 meses anteriores ao mês previsto. Desde o primeiro mês registrado, meses sem gastos contam como zero. Compras parceladas ficam fora da média: somamos apenas as parcelas que vencem no mês previsto.\n\nEm cada categoria, consideramos o maior valor entre a média e os gastos à vista já lançados; depois somamos as parcelas. Assim, evitamos contar duas vezes um gasto já previsto. São estimativas, não garantias de gastos futuros.")); how.Padding = new Thickness(6);
        yield return how;
    }
    /// <summary>
    /// Aba de recorrentes: a mesma lista do diálogo "Despesas recorrentes", direto na página.
    /// </summary>
    private IEnumerable<Control> RecurrencesTab()
    {
        yield return Text("Contas que se repetem todo mês. Cada mês é uma despesa comum: você pode pagar, editar ou excluir um mês sem afetar os outros.", "muted");
        if (data.Recurrences.Count == 0) yield return EmptyCard("Nenhuma despesa recorrente ainda", "Para aluguel, internet e assinaturas: ao criar uma despesa, marque “Repetir todo mês”.", Button("Nova despesa", () => _ = Run(() => EditExpense()), "ink", "Plus"));
        // Na página, erros vão para o aviso global e a lista se refaz sozinha quando o salvamento reconstrói a tela.
        else yield return Card(RecurrenceRows(() => this, action => Run(action)));
    }
    /// <summary>
    /// Aba de acerto: quem deve a quem e o histórico de acertos já registrados.
    /// </summary>
    private IEnumerable<Control> SettlementTab()
    {
        if (data.People.Count < 2) { yield return EmptyCard("O acerto precisa de duas pessoas", "Quando mais de uma pessoa divide as despesas, o BankManager mostra aqui quem deve a quem. Cadastre outra pessoa em Pessoas e rendas."); yield break; }
        yield return SettlementCard(history: false);
        var historyPanel = Stack(Text("Histórico de acertos", "section"), SettlementRows(() => this, () => { })); historyPanel.Spacing = 8;
        yield return Card(historyPanel);
    }
}
