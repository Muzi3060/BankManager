using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Diálogo "Despesas recorrentes": lista os modelos mensais e permite encerrar ou remover cada um.
public sealed partial class MainWindow
{
    /// <summary>
    /// Linhas da lista de recorrências, usadas no diálogo e na aba de "Previsões e metas". Encerrar para a geração depois do mês
    /// selecionado; remover um modelo já encerrado o tira da lista sem apagar o histórico.
    /// </summary>
    /// <param name="owner">Janela sobre a qual as confirmações abrem.</param>
    /// <param name="act">Executa a ação com o tratamento de erros de quem chama (e, no diálogo, refaz a lista).</param>
    private Control RecurrenceRows(Func<Window> owner, Func<Func<Task>, Task> act)
    {
        var rows = new StackPanel();
        foreach (var recurrence in data.Recurrences.OrderBy(r => r.EndMonth is not null).ThenBy(r => r.Title))
        {
            if (rows.Children.Count > 0) rows.Children.Add(Divider());
            var detail = $"{recurrence.Category} · todo dia {recurrence.DayOfMonth} · {Money.Format(recurrence.Amount)} · desde {recurrence.StartMonth.ToString("MM/yyyy")}";
            var who = Stack(new TextBlock { Text = recurrence.Title, FontWeight = FontWeight.SemiBold, FontSize = 13 }, Text(detail, "muted").WithClass("small")); who.Spacing = 1; who.VerticalAlignment = VerticalAlignment.Center; who.Margin = new Thickness(12, 0);
            Control status = recurrence.EndMonth is { } end ? Chip($"Encerrada em {end:MM/yyyy}", "status") : Chip("Ativa", "positive");
            status.Margin = new Thickness(0, 0, 12, 0);
            var id = recurrence.Id;
            var action = recurrence.EndMonth is null
                ? Button("Encerrar", () => _ = act(async () =>
                {
                    // O mês selecionado na janela principal vira a última cobrança; os meses em aberto depois dele saem.
                    var last = month.ToString("MMMM 'de' yyyy", Money.Culture);
                    if (await Confirm(owner(), "Encerrar recorrência?", $"Encerrar “{recurrence.Title}”? A última cobrança será em {last} (o mês selecionado). Meses em aberto depois dele são removidos; o que já foi pago continua no histórico.")) Commit(d => RecurrenceEngine.End(d, id, month));
                }))
                : Button("Remover", () => _ = act(async () =>
                {
                    if (await Confirm(owner(), "Remover recorrência?", $"Remover “{recurrence.Title}” da lista? Os lançamentos já criados continuam como despesas comuns.")) Commit(d => RecurrenceEngine.Remove(d, id));
                }), "ghost danger");
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(0, 10) };
            Grid.SetColumn(who, 1); Grid.SetColumn(status, 2); Grid.SetColumn(action, 3); row.Children.Add(IconCircle(CategoryIconName(recurrence.Category))); row.Children.Add(who); row.Children.Add(status); row.Children.Add(action); rows.Children.Add(row);
        }
        if (data.Recurrences.Count == 0) rows.Children.Add(Text("Nenhuma despesa recorrente ainda. Ao criar uma despesa, marque “Repetir todo mês”.", "muted"));
        return rows;
    }
    /// <summary>
    /// Abre a lista de despesas que se repetem todo mês em um diálogo (atalho a partir da página de despesas).
    /// </summary>
    private async Task ManageRecurrences()
    {
        var holder = new ContentControl(); var error = Text("", "error");
        Window? dialog = null;
        // Executa a ação, mostra um erro simples no próprio diálogo e atualiza a lista.
        async Task Act(Func<Task> action)
        {
            try { error.Text = ""; await action(); }
            catch (Exception ex) { error.Text = FriendlyError(ex); }
            holder.Content = RecurrenceRows(() => dialog!, Act);
        }
        holder.Content = RecurrenceRows(() => dialog!, Act);
        var panel = Stack(Heading("Despesas recorrentes", "Contas que se repetem todo mês. Cada mês é uma despesa comum: você pode pagar, editar ou excluir um mês sem afetar os outros."), holder, error);
        dialog = Dialog("Despesas recorrentes", panel, 720); dialog.Height = 560;
        panel.Children.Add(Row(Button("Fechar", () => dialog.Close(), "primary")));
        await dialog.ShowDialog(this);
    }
}
