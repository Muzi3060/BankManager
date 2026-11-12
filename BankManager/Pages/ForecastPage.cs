using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Página "Previsões": estimativa do mês seguinte ao selecionado.
public sealed partial class MainWindow
{
    /// <summary>
    /// Exibe a previsão para o mês seguinte ao selecionado, por categoria, e explica (em uma seção que abre e fecha) como o valor é calculado.
    /// </summary>
    private Control ForecastPage()
    {
        var target = month.AddMonths(1); var predictions = FinanceEngine.Forecasts(data, target);
        var panel = Stack(PageHeader("Previsões", $"Estimativa para {target.ToString("MMMM 'de' yyyy", Money.Culture)}, a partir do histórico até o mês selecionado.")); panel.Spacing = 20;
        var rows = new StackPanel { Spacing = 0 };
        if (predictions.Count == 0)
        {
            var empty = Stack(Text("Ainda não há o que prever", "section"), Text("Adicione despesas para começar. Um mês com R$ 1.000,00 em mercado gera uma estimativa de R$ 1.000,00 para o próximo mês.", "muted")); empty.Spacing = 8; empty.Margin = new Thickness(24, 32);
            rows.Children.Add(empty);
        }
        foreach (var prediction in predictions)
        {
            if (rows.Children.Count > 0) rows.Children.Add(Divider());
            var info = Stack(new TextBlock { Text = prediction.Category, FontWeight = FontWeight.Medium }, Text($"Média {Money.Format(prediction.Historical)} · {prediction.Months} {(prediction.Months == 1 ? "mês" : "meses")} de base · já lançado {Money.Format(prediction.Scheduled)}", "muted")); info.Spacing = 0; info.VerticalAlignment = VerticalAlignment.Center;
            var amount = Text(Money.Format(prediction.Estimate), "num"); amount.FontWeight = FontWeight.SemiBold; amount.VerticalAlignment = VerticalAlignment.Center; amount.TextAlignment = TextAlignment.Right; amount.Width = 110;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,150,Auto"), Margin = new Thickness(20, 12) }; row.Children.Add(info);
            if (prediction.Months < 3) { var chip = Chip("Pouco histórico", "warn"); chip.HorizontalAlignment = HorizontalAlignment.Right; chip.Margin = new Thickness(0, 0, 12, 0); Grid.SetColumn(chip, 1); row.Children.Add(chip); }
            Grid.SetColumn(amount, 2); row.Children.Add(amount); rows.Children.Add(row);
        }
        var table = Card(rows); table.Padding = new Thickness(0); panel.Children.Add(table);
        var how = Card(Collapsible("Como chegamos a esses valores", "Usamos a média de até 3 meses anteriores ao mês previsto. Desde o primeiro mês registrado, meses sem gastos contam como zero. Compras parceladas ficam fora da média: somamos apenas as parcelas que vencem no mês previsto.\n\nEm cada categoria, consideramos o maior valor entre a média e os gastos à vista já lançados; depois somamos as parcelas. Assim, evitamos contar duas vezes um gasto já previsto. São estimativas, não garantias de gastos futuros.")); how.Padding = new Thickness(6); panel.Children.Add(how);
        return panel;
    }
}
