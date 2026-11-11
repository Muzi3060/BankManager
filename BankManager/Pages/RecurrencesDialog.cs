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
    /// Abre a lista de despesas que se repetem todo mês. Encerrar para a geração depois do mês selecionado;
    /// remover um modelo já encerrado o tira da lista sem apagar o histórico.
    /// </summary>
    private async Task ManageRecurrences()
    {
        var holder = new ContentControl(); var error = Text("", "error");
        Window? dialog = null;
        // Reconstrói a lista a partir dos dados atuais; chamada de novo depois de cada ação.
        Control Build()
        {
            var rows = new StackPanel();
            foreach (var recurrence in data.Recurrences.OrderBy(r => r.EndMonth is not null).ThenBy(r => r.Title))
            {
                if (rows.Children.Count > 0) rows.Children.Add(Divider());
                var detail = $"{recurrence.Category} · todo dia {recurrence.DayOfMonth} · {Money.Format(recurrence.Amount)} · desde {recurrence.StartMonth.ToString("MM/yyyy")}";
                var who = Stack(new TextBlock { Text = recurrence.Title, FontWeight = FontWeight.Medium }, Text(detail, "muted")); who.Spacing = 0; who.VerticalAlignment = VerticalAlignment.Center;
                Control status = recurrence.EndMonth is { } end ? Chip($"Encerrada em {end:MM/yyyy}") : Chip("Ativa", "positive");
                status.Margin = new Thickness(12, 0);
                var id = recurrence.Id;
                var action = recurrence.EndMonth is null
                    ? Button("Encerrar", () => _ = Run(() => Act(async () =>
                    {
                        // O mês selecionado na janela principal vira a última cobrança; os meses em aberto depois dele saem.
                        var last = month.ToString("MMMM 'de' yyyy", Money.Culture);
                        if (await Confirm(dialog!, "Encerrar recorrência?", $"Encerrar “{recurrence.Title}”? A última cobrança será em {last} (o mês selecionado). Meses em aberto depois dele são removidos; o que já foi pago continua no histórico.")) Commit(d => RecurrenceEngine.End(d, id, month));
                    })))
                    : Button("Remover", () => _ = Run(() => Act(async () =>
                    {
                        if (await Confirm(dialog!, "Remover recorrência?", $"Remover “{recurrence.Title}” da lista? Os lançamentos já criados continuam como despesas comuns.")) Commit(d => RecurrenceEngine.Remove(d, id));
                    })), "ghost danger");
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 12) };
                Grid.SetColumn(status, 1); Grid.SetColumn(action, 2); row.Children.Add(who); row.Children.Add(status); row.Children.Add(action); rows.Children.Add(row);
            }
            if (data.Recurrences.Count == 0) rows.Children.Add(Text("Nenhuma despesa recorrente ainda. Ao criar uma despesa, marque “Repetir todo mês”.", "muted"));
            return rows;
        }
        // Executa a ação, mostra um erro simples no próprio diálogo e atualiza a lista.
        async Task Act(Func<Task> action)
        {
            try { error.Text = ""; await action(); }
            catch (Exception ex) { error.Text = FriendlyError(ex); }
            holder.Content = Build();
        }
        holder.Content = Build();
        var panel = Stack(Heading("Despesas recorrentes", "Contas que se repetem todo mês. Cada mês é uma despesa comum: você pode pagar, editar ou excluir um mês sem afetar os outros."), holder, error);
        dialog = Dialog("Despesas recorrentes", panel, 720); dialog.Height = 560;
        panel.Children.Add(Row(Button("Fechar", () => dialog.Close(), "primary")));
        await dialog.ShowDialog(this);
    }
}
