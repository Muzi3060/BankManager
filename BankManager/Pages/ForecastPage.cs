using Avalonia.Controls;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Página "Previsões": estimativa do mês seguinte ao selecionado.
public sealed partial class MainWindow
{
    /// <summary>
    /// Exibe a previsão para o mês seguinte ao selecionado e explica a base histórica e os compromissos considerados.
    /// </summary>
    private Control ForecastPage()
    {
        var target = month.AddMonths(1); var predictions = FinanceEngine.Forecasts(data, target);
        var panel = Stack(Heading("Olhe para o próximo mês", $"Estimativa para {target.ToString("MMMM 'de' yyyy", Money.Culture)} a partir do histórico até o fim do mês selecionado."),
            Columns(Metric("Estimativa total", predictions.Sum(f => f.Estimate), "Histórico + compromissos conhecidos", "accent"), Metric("Já lançado no próximo mês", FinanceEngine.Expenses(data, target).Sum(e => e.Amount), "Inclui parcelas com vencimento no mês")),
            Card(Stack(Text("Como chegamos a esses valores", "section"), Text("Usamos a média de até 3 meses anteriores ao mês previsto. Desde o primeiro mês registrado, meses sem gastos contam como zero. Compras parceladas ficam fora da média: somamos apenas as parcelas que vencem no mês previsto.\n\nEm cada categoria, consideramos o maior valor entre a média e os gastos à vista já lançados; depois somamos as parcelas. Assim, evitamos contar duas vezes um gasto já previsto. São estimativas, não garantias de gastos futuros.", "muted"))));
        if (predictions.Count == 0) panel.Children.Add(Card(Text("Adicione despesas para começar. Um mês com R$ 1.000,00 em mercado gera uma estimativa de R$ 1.000,00 para o próximo mês.", "muted")));
        foreach (var prediction in predictions)
            panel.Children.Add(Card(Columns(Stack(Text(prediction.Category, "section"), Text($"Média histórica: {Money.Format(prediction.Historical)} · {prediction.Months} mês(es) de base\nJá lançado: {Money.Format(prediction.Scheduled)}", "muted")), Stack(Text(Money.Format(prediction.Estimate), "money"), Text(prediction.Months < 3 ? "Pouco histórico: acompanhe de perto" : "Estimativa baseada em 3 meses", "muted")))));
        return panel;
    }
}
