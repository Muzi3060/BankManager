using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BankManager.Core;
using static BankManager.Ui;

namespace BankManager;
// Compartilhar despesas, parte de quem recebe: o assistente "Importar despesas compartilhadas", em três passos (Arquivo, Pessoas,
// Prévia) mais a tela de resultado. As despesas são ADICIONADAS; nada existente é substituído. Toda a lógica vem de ExpenseShare (Core).
public sealed partial class MainWindow
{
    /// <summary>
    /// Pede um arquivo de compartilhamento ao seletor do sistema (ou usa o caminho de teste). Nulo quando a pessoa desiste.
    /// </summary>
    private async Task<string?> PickShareFile()
    {
        if (TestOpenPath is not null) return TestOpenPath;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Importar despesas compartilhadas", AllowMultiple = false, FileTypeFilter = [ShareFiles, new FilePickerFileType("Arquivos JSON") { Patterns = ["*.json"] }] });
        return files.Count == 0 ? null : files[0].TryGetLocalPath() ?? throw new BudgetException("Escolha um arquivo local.");
    }
    /// <summary>
    /// Indicador de passos "1 Arquivo — 2 Pessoas — 3 Prévia": o atual em círculo preto com o número, os concluídos em verde com ✓.
    /// </summary>
    private static Control StepIndicator(int current)
    {
        string[] labels = ["Arquivo", "Pessoas", "Prévia"]; var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        for (var i = 0; i < labels.Length; i++)
        {
            var state = i + 1 < current ? "done" : i + 1 == current ? "current" : null;
            var circle = new Border { Child = state == "done" ? Icon("Check", 14) : new TextBlock { Text = (i + 1).ToString() } }.WithClass("step"); var label = Text(labels[i], "steplabel");
            if (state is not null) { circle.Classes.Add(state); label.Classes.Add(state); }
            if (i > 0) row.Children.Add(new Border { Width = 28, VerticalAlignment = VerticalAlignment.Center }.WithClass("divider"));
            row.Children.Add(circle); row.Children.Add(label);
        }
        AutomationProperties.SetName(row, $"Passo {current} de 3: {labels[current - 1]}"); return row;
    }
    /// <summary>
    /// Abre o assistente de importação. Primeiro o seletor de arquivo; depois, os passos dentro de um diálogo sobreposto. As escolhas
    /// ficam guardadas enquanto o assistente está aberto, então voltar um passo nunca as perde. Só o botão final grava, de uma vez.
    /// </summary>
    private async Task ImportShared()
    {
        if (!ready || store is null) throw new BudgetException("Os dados ainda não estão disponíveis.");
        if (OverlayOpen) return;
        var picked = await PickShareFile(); if (picked is null) return;
        // ----- Estado do assistente (vive nesta chamada; cada passo é redesenhado a partir dele) -----
        var path = picked; ShareFile? file = null; string? fileError = null; long size = 0;
        var localNames = data.People.Select(p => p.Name).ToList();
        // Escolha por pessoa do arquivo: índice de uma pessoa existente, depois "criar" e "ignorar". Categorias: 0 cria, 1 usa "Outros".
        var personChoice = new Dictionary<string, int>(); var categoryChoice = new Dictionary<string, int>(); var fallback = 0;
        var equal = false; var anyway = new HashSet<Guid>(); var touched = false;
        int CreateIndex() => localNames.Count; int IgnoreIndex() => localNames.Count + 1;
        // Lê e valida o arquivo. Qualquer problema vira uma explicação em linguagem simples; nada é alterado.
        void Load(string chosen)
        {
            path = chosen; file = null; fileError = null; personChoice.Clear(); categoryChoice.Clear(); fallback = 0; equal = false; anyway.Clear();
            try
            {
                RejectInternalPath(chosen); size = new FileInfo(chosen).Length; var read = ExpenseShare.Read(chosen); var suggestion = ExpenseShare.Suggest(data, read);
                foreach (var name in read.People) personChoice[name] = suggestion.People[name].Action == PersonAction.Existing ? data.People.FindIndex(p => p.Id == suggestion.People[name].PersonId) : CreateIndex();
                foreach (var name in ExpenseShare.MissingCategories(data, read)) categoryChoice[name] = suggestion.CreateCategories.GetValueOrDefault(name) ? 0 : 1;
                file = read;
            }
            catch (Exception ex)
            {
                var reason = ex is BudgetException ? ex.Message : "Não foi possível ler o arquivo. Confira se ele ainda existe e se você tem permissão para abri-lo.";
                fileError = reason.Contains("Nada foi alterado") || reason.Contains("foram preservados") ? reason : reason + " Nada foi alterado nos seus dados.";
            }
        }
        // Traduz as escolhas da tela para o mapeamento que o Core entende.
        ShareMapping Mapping() => new()
        {
            People = personChoice.ToDictionary(p => p.Key, p => p.Value < localNames.Count ? new PersonChoice(PersonAction.Existing, data.People[p.Value].Id) : new PersonChoice(p.Value == CreateIndex() ? PersonAction.Create : PersonAction.Ignore)),
            CreateCategories = categoryChoice.ToDictionary(c => c.Key, c => c.Value == 0), Fallback = fallback > 0 ? data.People[fallback - 1].Id : null,
            EqualInsteadOfProportional = equal, ImportAnyway = anyway.ToHashSet()
        };
        string Plural(int n, string one, string many) => n == 1 ? one : many;
        // Esc pede confirmação quando já há escolhas feitas; "Cancelar" e o "X" são pedidos explícitos e fecham direto.
        async void Escape()
        {
            if (!touched || await Confirm(this, "Sair da importação?", "As escolhas feitas neste assistente serão descartadas. Nada foi salvo e seus dados continuam como estavam.")) CloseOverlay();
        }
        void Step1()
        {
            var change = Button("Trocar arquivo", () => _ = Run(async () => { var other = await PickShareFile(); if (other is null) return; Load(other); touched = false; Step1(); })); Grid.SetColumn(change, 2); change.VerticalAlignment = VerticalAlignment.Center;
            var info = Stack(Text(Path.GetFileName(path), "mono").WithClass("strong"), Text($"Escolhido agora · {(size < 1024 ? $"{size} bytes" : size < 1024 * 1024 ? $"{Math.Max(1, size / 1024)} KB" : $"{size / 1024d / 1024d:0.#} MB")}", "muted").WithClass("small")); info.Spacing = 2; info.VerticalAlignment = VerticalAlignment.Center; info.Margin = new Thickness(14, 0); Grid.SetColumn(info, 1);
            ((TextBlock)info.Children[0]).TextTrimming = TextTrimming.CharacterEllipsis;
            var fileCard = new Border { Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { new Border { Child = Icon("File", 20) }.WithClass("tile"), info, change } } }.WithClass("panel");
            var body = Stack(fileCard); body.Spacing = 14;
            var next = Button("Continuar", () => { touched = true; Step2(); }, "primary"); next.IsEnabled = file is not null;
            if (file is null) body.Children.Add(Banner("Alert", fileError ?? "Arquivo inválido.", "negative"));
            else
            {
                body.Children.Add(Banner("Check", "Arquivo válido, gerado pelo BankManager" + (file.AppVersion.Length > 0 ? " " + file.AppVersion : ""), "positive"));
                var missing = categoryChoice.Keys.ToList();
                var left = Stack(SummaryLine("Despesas", file.Expenses.Count.ToString()), SummaryLine("Pessoas no arquivo", string.Join(", ", file.People))); left.Spacing = 0; left.Margin = new Thickness(0, 0, 14, 0);
                var right = Stack(SummaryLine("Total", Money.Format(file.Expenses.Sum(e => e.Amount))), SummaryLine(Plural(missing.Count, "Categoria que você pode não ter", "Categorias que você pode não ter"), missing.Count == 0 ? "Nenhuma" : string.Join(", ", missing))); right.Spacing = 0; right.Margin = new Thickness(14, 0, 0, 0); Grid.SetColumn(right, 1);
                body.Children.Add(new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Children = { left, right } });
                body.Children.Add(Banner("Info", "Antes de salvar, você confere tudo na prévia. Um backup da versão anterior dos seus dados é guardado automaticamente."));
            }
            var (card, heading) = ModalCard("Importar despesas compartilhadas", "As despesas serão adicionadas aos seus dados, sem apagar nem trocar nada.", body, ModalFooter(null, Button("Cancelar", CloseOverlay), next), CloseOverlay, StepIndicator(1));
            ShowOverlay(card, Escape, heading);
        }
        void Step2()
        {
            var shared = file!; var error = Text("", "error"); error.IsVisible = false;
            var fallbackBox = Combo(new[] { "Escolha uma pessoa…" }.Concat(localNames), fallback);
            var fallbackField = Field("Despesas que ficarem sem responsável vão para", fallbackBox, "Usado quando todas as pessoas de uma despesa foram ignoradas."); AutomationProperties.SetName(fallbackBox, "Quem assume as despesas sem responsável");
            fallbackBox.SelectionChanged += (_, _) => { if (fallbackBox.SelectedIndex >= 0) { fallback = fallbackBox.SelectedIndex; touched = true; error.IsVisible = false; } };
            void ShowFallback() => fallbackField.IsVisible = personChoice.Values.Any(v => v == IgnoreIndex());
            // Linha "no arquivo → aqui": ícone ou avatar, nome, seta, lista de escolhas e, se houver, um chip com a situação.
            Control MapRow(Control lead, string name, string caption, ComboBox box, Control? chip)
            {
                var who = Stack(new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, Text(caption, "muted").WithClass("small")); who.Spacing = 1; who.VerticalAlignment = VerticalAlignment.Center; who.Margin = new Thickness(12, 0, 8, 0);
                var arrow = Icon("ArrowRight", 16); arrow.Margin = new Thickness(0, 0, 14, 0); box.VerticalAlignment = VerticalAlignment.Center;
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,170,Auto,*,Auto"), Margin = new Thickness(0, 10) };
                Control[] cells = chip is null ? [lead, who, arrow, box] : [lead, who, arrow, box, chip];
                for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); grid.Children.Add(cells[i]); }
                return grid;
            }
            var body = Stack(Text("Pessoas", "section"), Divider()); body.Spacing = 0;
            foreach (var name in shared.People)
            {
                var box = Combo(localNames.Concat([$"Criar nova pessoa “{name}”", "Ignorar esta pessoa"]), personChoice[name]); AutomationProperties.SetName(box, "Pessoa: " + name);
                var chipHolder = new ContentControl { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), MinWidth = 96 };
                // Chip: "Mesmo nome" quando a pessoa escolhida tem o mesmo nome (sem diferenciar maiúsculas e acentos), "Nova pessoa" ou "Ignorada".
                void ShowChip()
                {
                    var choice = personChoice[name];
                    chipHolder.Content = choice == CreateIndex() ? Chip("Nova pessoa", "status") : choice == IgnoreIndex() ? Chip("Ignorada", "warn") : ExpenseSearch.Fold(localNames[choice]) == ExpenseSearch.Fold(name) ? Chip("Mesmo nome", "positive") : Chip("Outra pessoa", "status");
                }
                box.SelectionChanged += (_, _) => { if (box.SelectedIndex < 0) return; personChoice[name] = box.SelectedIndex; touched = true; error.IsVisible = false; ShowChip(); ShowFallback(); };
                ShowChip(); body.Children.Add(MapRow(Avatar(name).WithClass("large"), name, "no arquivo", box, chipHolder)); body.Children.Add(Divider());
            }
            var help = Text("Se você ignorar alguém, as despesas dessa pessoa ficam com os outros responsáveis; as que ficarem sem ninguém vão para quem você escolher aqui. Nenhuma despesa fica sem responsável.", "muted"); help.Classes.Add("small"); help.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(help); fallbackField.Margin = new Thickness(0, 12, 0, 0); body.Children.Add(fallbackField); ShowFallback();
            if (categoryChoice.Count > 0)
            {
                var title = Text("Categorias", "section"); title.Margin = new Thickness(0, 22, 0, 12); body.Children.Add(title); body.Children.Add(Divider());
                foreach (var name in categoryChoice.Keys.ToList())
                {
                    var box = Combo([$"Criar a categoria “{name}”", "Usar “Outros”"], categoryChoice[name]); AutomationProperties.SetName(box, "Categoria: " + name);
                    box.SelectionChanged += (_, _) => { if (box.SelectedIndex < 0) return; categoryChoice[name] = box.SelectedIndex; touched = true; error.IsVisible = false; };
                    body.Children.Add(MapRow(new Border { Child = Icon("Tag", 18), Background = null }.WithClass("tile").WithClass("panel"), name, "você ainda não tem", box, null)); body.Children.Add(Divider());
                }
            }
            error.Margin = new Thickness(0, 12, 0, 0); body.Children.Add(error);
            // Continuar só avança se o mapeamento funciona de verdade: o cálculo é o mesmo da prévia, em uma cópia dos dados.
            var next = Button("Continuar", () =>
            {
                try { ExpenseShare.Import(data, shared, Mapping()); Step3(); }
                catch (Exception ex) { error.Text = FriendlyError(ex); error.IsVisible = true; error.BringIntoView(); }
            }, "primary");
            var (card, heading) = ModalCard("Quem é quem?", "Os nomes do arquivo podem ser diferentes dos seus. Confirme cada um.", body, ModalFooter(Button("Voltar", Step1), Button("Cancelar", CloseOverlay), next), CloseOverlay, StepIndicator(2));
            ShowOverlay(card, Escape, heading);
        }
        void Step3()
        {
            var shared = file!; var summary = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; var error = new ContentControl();
            var add = Button("Adicionar", () => { }, "primary"); ShareResult? preview = null;
            // Uma linha por despesa do arquivo. Os controles são criados uma vez e só atualizados, para o foco não se perder ao marcar uma caixa.
            var rows = shared.Expenses.Select(source =>
            {
                var title = new TextBlock { Text = source.Title, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis }; var detail = Text("", "muted"); detail.Classes.Add("small");
                var force = new CheckBox { Content = "Importar mesmo assim, como outra despesa", IsChecked = anyway.Contains(source.OriginId), IsVisible = false, FontSize = 13 }; AutomationProperties.SetName(force, "Importar mesmo assim: " + source.Title);
                var text = Stack(title, detail, force); text.Spacing = 1; text.VerticalAlignment = VerticalAlignment.Center;
                var chip = new ContentControl { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) }; Grid.SetColumn(chip, 1);
                var amount = Text(Money.Format(source.Amount), "strong"); amount.VerticalAlignment = VerticalAlignment.Center; amount.TextAlignment = TextAlignment.Right; amount.MinWidth = 100; Grid.SetColumn(amount, 2);
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 9), Children = { text, chip, amount } };
                return (Source: source, Title: title, Detail: detail, Force: force, Chip: chip, Amount: amount, View: grid);
            }).ToList();
            // Recalcula a prévia em uma cópia dos dados (nada é gravado) e atualiza chips, linhas e o botão final.
            void Refresh()
            {
                try { preview = ExpenseShare.Import(data, shared, Mapping()).Result; error.Content = null; }
                catch (Exception ex) { preview = null; error.Content = Banner("Alert", FriendlyError(ex), "negative"); }
                summary.Children.Clear(); var n = preview?.Added.Count ?? 0;
                if (preview is not null)
                {
                    summary.Children.Add(Chip($"{n} {Plural(n, "será adicionada", "serão adicionadas")} · {Money.Format(preview.Added.Sum(e => e.Amount))}", "positive"));
                    if (preview.Duplicates > 0) summary.Children.Add(Chip($"{preview.Duplicates} já {Plural(preview.Duplicates, "existe · será pulada", "existem · serão puladas")}", "warn"));
                    if (preview.Errors.Count > 0) summary.Children.Add(Chip($"{preview.Errors.Count} com erro · {Plural(preview.Errors.Count, "não será importada", "não serão importadas")}", "negative"));
                    foreach (var chip in summary.Children.OfType<Border>()) { chip.Padding = new Thickness(14, 7); ((TextBlock)chip.Child!).FontSize = 13; ((TextBlock)chip.Child!).FontWeight = FontWeight.SemiBold; }
                }
                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows[i]; var line = preview?.Lines[i]; var when = $"{row.Source.DueDate:dd/MM}" + (row.Source.DueDate.Year != DateTime.Today.Year ? $"/{row.Source.DueDate:yy}" : "");
                    var category = row.Source.Category + (row.Source.InstallmentCount > 1 ? $" · parcela {row.Source.InstallmentNumber} de {row.Source.InstallmentCount}" : "");
                    row.Force.IsVisible = line?.Existing == true; var skipped = line?.Status != ShareLineStatus.Added;
                    row.Title.Classes.Set("muted", skipped); row.Amount.Classes.Set("muted", skipped);
                    row.Chip.Content = line?.Status switch { ShareLineStatus.Added => Chip("Adicionar", "positive"), ShareLineStatus.Skipped => Chip("Pular", "warn"), ShareLineStatus.Error => Chip("Com erro", "negative"), _ => null };
                    row.Detail.Text = line?.Status switch
                    {
                        // Pessoas como ficarão aqui (depois do mapeamento), ligadas por "e".
                        ShareLineStatus.Added => $"{line.Expense!.Category}{category[row.Source.Category.Length..]} · {when} · {string.Join(" e ", row.Source.Participants.Where(p => personChoice[p] != IgnoreIndex()).Select(p => personChoice[p] < localNames.Count ? localNames[personChoice[p]] : p).DefaultIfEmpty(fallback > 0 ? localNames[fallback - 1] : "—"))}",
                        ShareLineStatus.Skipped => $"{category} · {when} · você já tem esta despesa",
                        ShareLineStatus.Error => $"{category} · {when} · {line.Error}",
                        _ => $"{category} · {when}"
                    };
                }
                var label = n == 0 ? "Nada a adicionar" : $"Adicionar {n} {Plural(n, "despesa", "despesas")}"; add.Content = label; AutomationProperties.SetName(add, label); add.IsEnabled = n > 0;
            }
            var list = new StackPanel();
            foreach (var row in rows)
            {
                list.Children.Add(Divider()); list.Children.Add(row.View);
                row.Force.IsCheckedChanged += (_, _) => { if (row.Force.IsChecked == true) anyway.Add(row.Source.OriginId); else anyway.Remove(row.Source.OriginId); touched = true; Refresh(); };
            }
            list.Children.Add(Divider());
            var body = Stack(summary, list, error); body.Spacing = 12;
            if (shared.Expenses.Any(e => e.Split == SplitMode.AvailableIncome))
            {
                // Divisão proporcional: o rateio depende da renda de cada pessoa NESTA base; quem importa decide se mantém o modo.
                var notice = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
                notice.Inlines!.Add(new Avalonia.Controls.Documents.Run("Divisão proporcional à renda. ") { FontWeight = FontWeight.SemiBold }); notice.Inlines.Add(new Avalonia.Controls.Documents.Run("O valor de cada um será recalculado com a renda dos seus dados, então pode ficar diferente do que a outra pessoa viu."));
                var info = Icon("Info", 18); info.VerticalAlignment = VerticalAlignment.Top; info.Margin = new Thickness(0, 1, 12, 0); Grid.SetColumn(notice, 1);
                var mode = Segmented(["Manter proporcional", "Dividir igualmente"], equal ? 1 : 0, i => { equal = i == 1; touched = true; Refresh(); }).WithClass("ink"); mode.Margin = new Thickness(30, 0, 0, 0);
                var block = Stack(new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { info, notice } }, mode); block.Spacing = 12;
                body.Children.Add(new Border { Child = block }.WithClass("panel"));
            }
            add.Click += (_, _) =>
            {
                try
                {
                    // Uma única gravação: a alteração é feita em uma cópia, o orçamento inteiro é validado e só então o arquivo é trocado
                    // (a versão anterior vira a cópia automática). Qualquer falha deixa tudo como estava.
                    ShareResult? result = null; var mapping = Mapping();
                    Commit(d => result = ExpenseShare.Apply(d, shared, mapping));
                    ShowResult(result!);
                }
                catch (Exception ex) { error.Content = Banner("Alert", (ex is BudgetException ? ex.Message : "Não foi possível salvar. Verifique o espaço em disco e as permissões da pasta de dados.") + " Nada foi alterado nos seus dados.", "negative"); }
            };
            Refresh();
            var (card, heading) = ModalCard("Confira antes de adicionar", "Nada foi salvo ainda. Seus dados atuais não serão alterados.", body, ModalFooter(Button("Voltar", Step2), Button("Cancelar", CloseOverlay), add), CloseOverlay, StepIndicator(3));
            ShowOverlay(card, Escape, heading);
        }
        // Tela final: o que entrou, o que foi pulado e o lembrete da cópia anterior. "Ver despesas" abre o mês da primeira despesa adicionada.
        void ShowResult(ShareResult result)
        {
            var n = result.Added.Count; var skipped = result.Duplicates + result.Errors.Count;
            var title = Text($"{n} {Plural(n, "despesa adicionada", "despesas adicionadas")}", "modal-title"); title.FontSize = 26; title.TextAlignment = TextAlignment.Center; title.Focusable = true; AutomationProperties.SetHeadingLevel(title, 1);
            var parts = new List<string>();
            if (result.Duplicates > 0) parts.Add($"{result.Duplicates} {Plural(result.Duplicates, "foi pulada porque você já tinha", "foram puladas porque você já tinha")}.");
            if (result.Errors.Count > 0) parts.Add($"{result.Errors.Count} {Plural(result.Errors.Count, "tinha erro e não foi importada", "tinham erro e não foram importadas")}.");
            parts.Add("Todo o resto dos seus dados continua como estava.");
            var sentence = Text(string.Join(" ", parts), "modal-sub"); sentence.TextAlignment = TextAlignment.Center; sentence.FontSize = 14;
            string Label(Expense e) => e.Title + (e.InstallmentCount > 1 ? $" (parcela {e.InstallmentNumber}/{e.InstallmentCount})" : "");
            var lines = new List<(string Label, string Value)> { ("Adicionadas", string.Join(", ", result.Added.Take(3).Select(Label)) + (n > 3 ? $" e mais {n - 3}" : "")) };
            if (result.NewPeople.Count > 0) lines.Add((Plural(result.NewPeople.Count, "Pessoa nova criada", "Pessoas novas criadas"), string.Join(", ", result.NewPeople)));
            if (result.NewCategories.Count > 0) lines.Add((Plural(result.NewCategories.Count, "Categoria nova criada", "Categorias novas criadas"), string.Join(", ", result.NewCategories)));
            lines.Add(("Total adicionado", Money.Format(result.Added.Sum(e => e.Amount))));
            var summary = new StackPanel(); for (var i = 0; i < lines.Count; i++) summary.Children.Add(SummaryLine(lines[i].Label, lines[i].Value, i < lines.Count - 1));
            var see = Button("Ver despesas", () =>
            {
                // Vai para a página Despesas, sem filtros, no mês da primeira despesa adicionada.
                month = Dates.Month(result.Added.Min(e => e.DueDate)); allMonths = false; filter = ""; statusFilter = 0; categoryFilter = 0; CloseOverlay(); GoTo(2);
            }, "primary");
            var buttons = Row(Button("Fechar", CloseOverlay), see); buttons.HorizontalAlignment = HorizontalAlignment.Center; buttons.Margin = new Thickness(0, 6, 0, 0);
            var content = Stack(new Border { Child = Icon("Check", 30) }.WithClass("successcircle"), title, sentence, new Border { Child = summary, Margin = new Thickness(0, 8, 0, 0) }.WithClass("panel"),
                Banner("Shield", "Uma cópia da versão anterior dos seus dados foi guardada. Dá para recuperá-la em Ajustes."), buttons); content.Spacing = 12;
            var card = new Border { Child = new ScrollViewer { Content = content }, Width = 560 }.WithClass("modal");
            AutomationProperties.SetName(card, title.Text!); AutomationProperties.SetControlTypeOverride(card, Avalonia.Automation.Peers.AutomationControlType.Window);
            Avalonia.Input.KeyboardNavigation.SetTabNavigation(card, Avalonia.Input.KeyboardNavigationMode.Cycle);
            ShowOverlay(card, CloseOverlay, title);
        }
        Load(path); ClearNotice(); Step1();
    }
}
