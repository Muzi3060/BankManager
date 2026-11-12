using System.Text.Json;
using System.Text.Json.Nodes;
using BankManager.Core;

// Executável de testes sem framework externo: registra cenários e retorna código não zero se algum falhar.
// As datas e os dados são sintéticos; os testes de disco usam apenas arquivos temporários.
var tests = new List<(string Name, Action Test)>();
var month = new DateOnly(2026, 10, 1);
// Cria uma base independente para cada cenário, com duas pessoas e rendas na competência fixa do teste.
BudgetData Family(decimal a = 3000, decimal b = 1000)
{
    var data = new BudgetData { People = [new() { Name = "Ana" }, new() { Name = "Bruno" }] };
    data.Incomes = [new() { PersonId = data.People[0].Id, Month = month, Amount = a }, new() { PersonId = data.People[1].Id, Month = month, Amount = b }]; return data;
}
// Fábrica de despesa: modo individual seleciona só Ana; os demais modos incluem as duas pessoas.
Expense Bill(BudgetData d, decimal amount, SplitMode split = SplitMode.AvailableIncome) => new() { Title = "Mercado", Category = "Mercado", Amount = amount, DueDate = month, Split = split, Participants = split == SplitMode.Individual ? [d.People[0].Id] : d.People.Select(p => p.Id).ToList() };
// Compara resultados exatos, inclusive decimal, e lança uma exceção que o executor registra como falha.
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
// Exige falha de negócio: outra exceção indica um defeito e não deve ser aceita como validação correta.
void Reject(Action action) { try { action(); } catch (BudgetException) { return; } throw new Exception("Expected validation rejection"); }
// Registra o cenário para execução posterior, permitindo que uma falha não interrompa os outros testes.
void Test(string name, Action action) => tests.Add((name, action));

// A divisão proporcional deve reproduzir o exemplo: disponíveis de 3.000/1.000 dividem 800 em 600/200.
Test("75/25 proportional allocation", () => { var d = Family(); var a = FinanceEngine.Allocate(d, Bill(d, 800)); Equal(600m, a.Amounts[d.People[0].Id]); Equal(200m, a.Amounts[d.People[1].Id]); });

// Uma conta individual de 2.000 reduz a disponibilidade de Ana para 1.000, igualando os pesos.
Test("Individual debts reduce disposable income", () => { var d = Family(); d.Expenses.Add(Bill(d, 2000, SplitMode.Individual)); var a = FinanceEngine.Allocate(d, Bill(d, 800)); Equal(400m, a.Amounts[d.People[0].Id]); Equal(400m, a.Amounts[d.People[1].Id]); });

// Quitar a conta individual não devolve esse dinheiro à capacidade mensal de pagar contas compartilhadas.
Test("Paid individual debts still reduce disposable income", () => { var d = Family(); var debt = Bill(d, 2000, SplitMode.Individual); debt.Paid = true; d.Expenses.Add(debt); Equal(400m, FinanceEngine.Allocate(d, Bill(d, 800)).Amounts[d.People[0].Id]); });

// Sem renda para ninguém, a divisão deve ser igual e acompanhada de um aviso explícito.
Test("Zero available income falls back explicitly", () => { var d = Family(0, 0); var a = FinanceEngine.Allocate(d, Bill(d, 101)); Equal(50.5m, a.Amounts[d.People[0].Id]); Equal(true, a.Warning is not null); });

// Despesas individuais acima da renda zeram o peso, sem criar participação negativa.
Test("Negative capacity contributes zero", () => { var d = Family(100, 1000); d.Expenses.Add(Bill(d, 200, SplitMode.Individual)); var a = FinanceEngine.Allocate(d, Bill(d, 400)); Equal(0m, a.Amounts[d.People[0].Id]); Equal(400m, a.Amounts[d.People[1].Id]); });

// O modo individual atribui todo o lançamento ao único participante.
Test("Individual expense entirely assigned", () => { var d = Family(); Equal(123.45m, FinanceEngine.Allocate(d, Bill(d, 123.45m, SplitMode.Individual)).Amounts[d.People[0].Id]); });

// O modo igual não usa a diferença de salário como peso.
Test("Equal split ignores salary", () => { var d = Family(); Equal(50m, FinanceEngine.Allocate(d, Bill(d, 100, SplitMode.Equal)).Amounts[d.People[0].Id]); });

// Verifica conservação do total, precisão de centavos e estabilidade do rateio em 1.000 amostras.
Test("Cent rounding preserves totals across 1000 samples", () =>
{
    // Semente fixa torna os valores reproduzíveis; os Guids variam, mas a repetição usa a mesma base de pessoas.
    var random = new Random(42);
    for (int n = 0; n < 1000; n++)
    {
        var d = Family(random.Next(0, 1000000), random.Next(0, 1000000)); var value = random.Next(1, 10000000) / 100m;
        var a = FinanceEngine.Allocate(d, Bill(d, value)); Equal(value, a.Amounts.Values.Sum());
        Equal(true, a.Amounts.Values.All(v => v >= 0 && decimal.Round(v, 2) == v));
        var again = FinanceEngine.Allocate(d, Bill(d, value)); Equal(a.Amounts[d.People[0].Id], again.Amounts[d.People[0].Id]);
    }
});

// Quem tem peso zero não pode receber nem o centavo residual do arredondamento.
Test("Zero weight never receives rounding remainder", () => { var d = Family(0, 100); var a = FinanceEngine.Allocate(d, Bill(d, .01m)); Equal(0m, a.Amounts[d.People[0].Id]); Equal(.01m, a.Amounts[d.People[1].Id]); });

// Rendas novas não alteram a competência anterior e não são propagadas para outra pessoa.
Test("Monthly incomes are isolated", () => { var d = Family(); d.Incomes.Add(new() { PersonId = d.People[0].Id, Month = month.AddMonths(1), Amount = 5000 }); Equal(3000m, FinanceEngine.Income(d, d.People[0].Id, month)); Equal(0m, FinanceEngine.Income(d, d.People[1].Id, month.AddMonths(1))); });

// O saldo desconta a responsabilidade uma vez, separando os valores já quitados.
Test("Dashboard paid and planned balances", () => { var d = Family(); var e = Bill(d, 800); e.Paid = true; d.Expenses.Add(e); var a = FinanceEngine.Balances(d, month)[0]; Equal(600m, a.Paid); Equal(2400m, a.Remaining); });

// Parcelamento deve preservar total, identificadores únicos e o dia de referência após fevereiro.
Test("Installments preserve total and month-end anchor", () => { var d = Family(); var e = Bill(d, 100); e.DueDate = new(2028, 1, 31); var parts = BudgetRules.Installments(e, 3); Equal(100m, parts.Sum(p => p.Amount)); Equal(33.34m, parts[0].Amount); Equal(new DateOnly(2028, 2, 29), parts[1].DueDate); Equal(new DateOnly(2028, 3, 31), parts[2].DueDate); Equal(3, parts.Select(p => p.Id).Distinct().Count()); });

// Parcelas de menos de um centavo e quantidades acima do limite precisam ser recusadas.
Test("Too-small and too-many installments rejected", () => { var d = Family(); Reject(() => BudgetRules.Installments(Bill(d, .01m), 2)); Reject(() => BudgetRules.Installments(Bill(d, 100), 121)); });

// Com apenas um mês de mercado, a estimativa do mês seguinte é o mesmo valor.
Test("One month market forecast equals actual", () => { var d = Family(); d.Expenses.Add(Bill(d, 1000)); Equal(1000m, FinanceEngine.Forecasts(d, month.AddMonths(1)).Single().Estimate); });

// Meses sem registros dentro da janela observada contam como zero na média.
Test("Forecast includes zero months in observed window", () => { var d = Family(); d.Expenses.Add(Bill(d, 900)); var forecast = FinanceEngine.Forecasts(d, month.AddMonths(3)).Single(); Equal(300m, forecast.Historical); Equal(3, forecast.Months); });

// Gastos anteriores aos três meses da janela não podem continuar na previsão.
Test("Forecast excludes older history", () => { var d = Family(); d.Expenses.Add(Bill(d, 900)); Equal(0, FinanceEngine.Forecasts(d, month.AddMonths(4)).Count); });

// Um compromisso à vista já previsto pela média não deve ser somado novamente.
Test("Scheduled ordinary expenses do not double count history", () => { var d = Family(); d.Expenses.Add(Bill(d, 1000)); var next = Bill(d, 600); next.DueDate = month.AddMonths(1); d.Expenses.Add(next); Equal(1000m, FinanceEngine.Forecasts(d, month.AddMonths(1)).Single().Estimate); next.Amount = 1300; Equal(1300m, FinanceEngine.Forecasts(d, month.AddMonths(1)).Single().Estimate); });

// Parcelas só são previstas nos seus vencimentos e desaparecem ao terminar a dívida.
Test("Installments counted only on due month", () => { var d = Family(); d.Expenses.AddRange(BudgetRules.Installments(Bill(d, 900), 3)); Equal(300m, FinanceEngine.Forecasts(d, month.AddMonths(1)).Single().Estimate); Equal(0, FinanceEngine.Forecasts(d, month.AddMonths(3)).Count); });

// Registros posteriores ao alvo não podem influenciar uma previsão de meses anteriores.
Test("Future records cannot leak into historical forecast", () => { var d = Family(); var e = Bill(d, 1000); e.DueDate = month.AddMonths(5); d.Expenses.Add(e); Equal(0, FinanceEngine.Forecasts(d, month.AddMonths(1)).Count); });

// A entrada brasileira aceita vírgula decimal e zero quando permitido.
Test("Brazilian money parses exactly", () => { Equal(1000.50m, Money.Parse("1000,50", "Valor")); Equal(0m, Money.Parse("0", "Valor")); });

// Rejeita formatos ambíguos, negativos, notação científica, símbolos e precisão além do limite.
Test("Ambiguous negative exponent and excessive precision rejected", () => { foreach (var input in new[] { "1.000", "1,000", "-1", "1e3", "NaN", "", "R$ 10", "1000000000", "1,234" }) Reject(() => Money.Parse(input, "Valor")); Reject(() => Money.Parse("0", "Valor", true)); });

// Nomes repetidos e responsáveis inexistentes devem impedir o salvamento.
Test("Duplicate names and invalid identity rejected", () => { var d = Family(); d.People[1].Name = " ana "; Reject(() => BudgetRules.Validate(d)); d.People[1].Name = "Bruno"; d.Expenses.Add(Bill(d, 10) with { Participants = [Guid.NewGuid()] }); Reject(() => BudgetRules.Validate(d)); });

// O modo individual exige um responsável; valores desconhecidos do enum também são inválidos.
Test("Participant count and enum validated", () => { var d = Family(); d.Expenses.Add(Bill(d, 10) with { Split = SplitMode.Individual }); Reject(() => BudgetRules.Validate(d)); d.Expenses[0].Split = (SplitMode)90; Reject(() => BudgetRules.Validate(d)); });

// Cada pessoa só pode ter uma renda por mês e as competências devem estar no intervalo suportado.
Test("Dates and income uniqueness validated", () => { var d = Family(); d.Incomes.Add(d.Incomes[0] with { }); Reject(() => BudgetRules.Validate(d)); d.Incomes.RemoveAt(2); d.Incomes[0].Month = new(1999, 1, 1); Reject(() => BudgetRules.Validate(d)); });

// O total de itens precisa coincidir com a despesa; um backup adulterado não pode impor outro total.
Test("Item arithmetic and tampered total validated", () => { var d = Family(); var e = Bill(d, 5); e.Items.Add(new() { Name = "Arroz", Quantity = 2, UnitPrice = 2.50m }); d.Expenses.Add(e); BudgetRules.Validate(d); e.Amount = 6; Reject(() => BudgetRules.Validate(d)); });

// Quantidade fracionária que resulta em meio centavo deve arredondar para longe de zero.
Test("Fractional items round away from zero", () => { Equal(.01m, new ExpenseItem { Name = "Peso", Quantity = .5m, UnitPrice = .01m }.Total); });

// Coleções nulas vindas de JSON devem causar erro de negócio, não acesso a referência nula.
Test("Null collections rejected", () => { var d = Family(); d.Expenses = null!; Reject(() => BudgetRules.Validate(d)); });

// Nomes não podem conter caracteres de controle que deformem a apresentação.
Test("Text control characters rejected", () => { var d = Family(); d.People[0].Name = "Ana\nBruno"; Reject(() => BudgetRules.Validate(d)); });

// Confere leitura após gravação, cópia anterior, preservação após falha e permissões privadas no Unix.
Test("Persistence roundtrip backup and invalid-save preservation", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid());
    try
    {
        using var store = new LocalStore(path); var d = Family(); d.Expenses.Add(Bill(d, 800)); store.Save(d);
        Equal(800m, store.Load().Expenses[0].Amount); d.Expenses[0].Amount = 900; store.Save(d);
        Equal(800m, LocalStore.Read(store.BackupPath).Expenses[0].Amount); d.Expenses[0].Amount = -1;
        Reject(() => store.Save(d)); Equal(900m, store.Load().Expenses[0].Amount);
        if (!OperatingSystem.IsWindows()) Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(store.FilePath));
    }
    finally { Directory.Delete(path, true); }
});

// Uma segunda instância cooperante não pode adquirir o mesmo arquivo de bloqueio.
Test("Second writer is prevented", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid());
    try { using var store = new LocalStore(path); Reject(() => { using var duplicate = new LocalStore(path); }); }
    finally { Directory.Delete(path, true); }
});

// Recusa JSON malformado, versão desconhecida, campos estranhos, registros nulos e arquivo muito grande.
Test("Malformed and unsupported JSON cannot import", () =>
{
    var path = Path.GetTempFileName();
    try
    {
        foreach (var json in new[] { "{", "null", "{\"Version\":4}", "{\"Version\":0}", "{\"UnknownField\":true}", "{\"People\":[null]}" }) { File.WriteAllText(path, json); Reject(() => LocalStore.Read(path)); }
        using (var stream = File.OpenWrite(path)) stream.SetLength(21 * 1024 * 1024);
        Reject(() => LocalStore.Read(path));
    }
    finally { File.Delete(path); }
});
// A situação de vencimento: pago prevalece; atraso começa no dia seguinte; "em breve" vale de hoje a 3 dias.
Test("Due status classification", () =>
{
    var d = Family(); var today = new DateOnly(2026, 11, 11);
    DueStatus At(int offset, bool paid = false) { var e = Bill(d, 10); e.DueDate = today.AddDays(offset); e.Paid = paid; return DueStatus.Of(e, today); }
    Equal(DueKind.Paid, At(-10, true).Kind); Equal(DueKind.Overdue, At(-1).Kind); Equal(-1, At(-1).Days);
    Equal(DueKind.DueSoon, At(0).Kind); Equal(DueKind.DueSoon, At(3).Kind); Equal(DueKind.Open, At(4).Kind); Equal(1, At(1).Days);
});

// A preferência de tema é gravada em settings.json e lida de volta; o padrão é seguir o sistema.
Test("Theme preference roundtrip and default", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid());
    try
    {
        Equal(ThemePreference.Auto, SettingsStore.Load(path).Theme);
        SettingsStore.Save(path, new AppSettings { Theme = ThemePreference.Dark });
        Equal(ThemePreference.Dark, SettingsStore.Load(path).Theme);
        Equal(true, File.ReadAllText(Path.Combine(path, SettingsStore.FileName)).Contains("\"Dark\""));
    }
    finally { Directory.Delete(path, true); }
});

// Preferência corrompida ou com valor desconhecido nunca impede abrir o app: volta ao padrão.
Test("Corrupt settings fall back to defaults", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid()); Directory.CreateDirectory(path);
    try
    {
        foreach (var json in new[] { "{", "null", "{\"Theme\":\"Roxo\"}", "{\"Theme\":99}", "" })
        {
            File.WriteAllText(Path.Combine(path, SettingsStore.FileName), json);
            Equal(ThemePreference.Auto, SettingsStore.Load(path).Theme);
        }
    }
    finally { Directory.Delete(path, true); }
});

// Trocar o tema não pode tocar nos dados financeiros: o financas.json fica byte a byte igual.
Test("Saving settings leaves financial data untouched", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid());
    try
    {
        using var store = new LocalStore(path); var d = Family(); d.Expenses.Add(Bill(d, 800)); store.Save(d);
        var before = File.ReadAllBytes(store.FilePath);
        SettingsStore.Save(path, new AppSettings { Theme = ThemePreference.Light });
        Equal(true, before.SequenceEqual(File.ReadAllBytes(store.FilePath)));
        Equal(false, File.ReadAllText(store.FilePath).Contains("Theme"));
    }
    finally { Directory.Delete(path, true); }
});

// ---- Esquema v2: despesas recorrentes ----
var today = new DateOnly(2026, 11, 11);
// Modelo de aluguel que vence no dia 31, útil para testar meses curtos; começa em janeiro de 2026.
Recurrence Rent(BudgetData d, int day = 31, DateOnly? start = null) => new() { Title = "Aluguel", Category = "Moradia", Amount = 1000, DayOfMonth = day, Split = SplitMode.Equal, Participants = d.People.Select(p => p.Id).ToList(), StartMonth = start ?? new DateOnly(2026, 1, 1) };
// Reproduz um arquivo real da versão 1: sem a lista de recorrências e sem os campos novos das despesas.
string V1Json(BudgetData d)
{
    var node = JsonNode.Parse(JsonSerializer.Serialize(d, LocalStore.JsonOptions))!; node["Version"] = 1; node.AsObject().Remove("Recurrences");
    foreach (var e in node["Expenses"]!.AsArray()) { e!.AsObject().Remove("RecurrenceId"); e.AsObject().Remove("RecurrenceMonth"); }
    return node.ToJsonString();
}

// Um arquivo v1 é lido sem perda, ganha a versão atual e, no primeiro salvamento, o v1 original vira o backup.
Test("Version 1 file migrates to the current version without losing data", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid()); Directory.CreateDirectory(path);
    try
    {
        var d = Family(); d.Expenses.Add(Bill(d, 800)); File.WriteAllText(Path.Combine(path, "financas.json"), V1Json(d));
        using var store = new LocalStore(path); var loaded = store.Load();
        Equal(BudgetData.CurrentVersion, loaded.Version); Equal(2, loaded.People.Count); Equal(800m, loaded.Expenses[0].Amount); Equal(0, loaded.Recurrences.Count);
        store.Save(loaded);
        Equal(1, (int)JsonNode.Parse(File.ReadAllText(store.BackupPath))!["Version"]!); Equal(BudgetData.CurrentVersion, (int)JsonNode.Parse(File.ReadAllText(store.FilePath))!["Version"]!);
        Equal(800m, store.Load().Expenses[0].Amount);
    }
    finally { Directory.Delete(path, true); }
});

// Um arquivo que diz ser v1 mas traz recorrências está adulterado: é recusado em vez de adivinhado.
Test("Version 1 file with recurrences is rejected", () =>
{
    var d = Family(); d.Recurrences.Add(Rent(d)); var path = Path.GetTempFileName();
    try { var node = JsonNode.Parse(JsonSerializer.Serialize(d, LocalStore.JsonOptions))!; node["Version"] = 1; File.WriteAllText(path, node.ToJsonString()); Reject(() => LocalStore.Read(path)); }
    finally { File.Delete(path); }
});

// Do mês inicial ao horizonte (12 meses à frente), dia 31 vira o último dia dos meses curtos, e repetir não duplica nada.
Test("Recurrence generates months up to the horizon and is idempotent", () =>
{
    var d = Family(); d.Recurrences.Add(Rent(d));
    Equal(23, RecurrenceEngine.Generate(d, today)); // jan/2026 a nov/2027
    Equal(0, RecurrenceEngine.Generate(d, today));
    var due = d.Expenses.ToDictionary(e => e.RecurrenceMonth!.Value, e => e.DueDate);
    Equal(new DateOnly(2026, 2, 28), due[new DateOnly(2026, 2, 1)]); Equal(new DateOnly(2026, 3, 31), due[new DateOnly(2026, 3, 1)]); Equal(new DateOnly(2027, 11, 30), due[new DateOnly(2027, 11, 1)]);
    Equal(false, d.Expenses.Any(e => e.Paid)); BudgetRules.Validate(d);
    // O rateio das ocorrências usa as regras de sempre: a soma fecha com o valor da despesa.
    Equal(1000m, FinanceEngine.Allocate(d, d.Expenses[0]).Amounts.Values.Sum());
});

// Com o passar do tempo, a janela avança e só os meses novos são criados.
Test("Recurrence tops up as time passes", () =>
{
    var d = Family(); d.Recurrences.Add(Rent(d)); RecurrenceEngine.Generate(d, today);
    Equal(2, RecurrenceEngine.Generate(d, new DateOnly(2027, 1, 5))); // dez/2027 e jan/2028
    Equal(25, d.Expenses.Count);
});

// O horizonte respeita o limite de 2099 e um início distante ainda cria o primeiro mês.
Test("Horizon clamp and far-future start", () =>
{
    Equal(new DateOnly(2099, 12, 1), RecurrenceEngine.Horizon(new DateOnly(2099, 6, 15)));
    var d = Family(); d.Recurrences.Add(Rent(d, 5, new DateOnly(2030, 1, 1)));
    Equal(1, RecurrenceEngine.Generate(d, today)); Equal(new DateOnly(2030, 1, 5), d.Expenses[0].DueDate);
});

// Excluir um mês de uma recorrência registra o mês como ignorado: a geração não o recria.
Test("Deleted occurrence is not regenerated", () =>
{
    var d = Family(); d.Recurrences.Add(Rent(d)); RecurrenceEngine.Generate(d, today);
    var march = d.Expenses.First(e => e.RecurrenceMonth == new DateOnly(2026, 3, 1)); RecurrenceEngine.RemoveExpense(d, march.Id);
    Equal(0, RecurrenceEngine.Generate(d, today)); Equal(22, d.Expenses.Count); Equal(1, d.Recurrences[0].SkippedMonths.Count); BudgetRules.Validate(d);
});

// Encerrar remove só o que está em aberto depois do mês final; o que já foi pago permanece no histórico.
Test("Ending a recurrence keeps paid history", () =>
{
    var d = Family(); d.Recurrences.Add(Rent(d)); RecurrenceEngine.Generate(d, today);
    var december = d.Expenses.First(e => e.RecurrenceMonth == new DateOnly(2026, 12, 1)); december.Paid = true;
    RecurrenceEngine.End(d, d.Recurrences[0].Id, new DateOnly(2026, 11, 20));
    Equal(new DateOnly(2026, 11, 1), d.Recurrences[0].EndMonth!.Value); Equal(11 + 1, d.Expenses.Count); // jan..nov + dez pago
    Equal(0, RecurrenceEngine.Generate(d, today)); BudgetRules.Validate(d);
    Reject(() => RecurrenceEngine.End(d, d.Recurrences[0].Id, new DateOnly(2026, 12, 1)));
});

// Não se encerra antes do início; remover o modelo mantém os lançamentos como despesas comuns.
Test("End before start is rejected and removing the model keeps expenses", () =>
{
    var d = Family(); d.Recurrences.Add(Rent(d)); RecurrenceEngine.Generate(d, today);
    Reject(() => RecurrenceEngine.End(d, d.Recurrences[0].Id, new DateOnly(2025, 12, 1)));
    var count = d.Expenses.Count; RecurrenceEngine.Remove(d, d.Recurrences[0].Id);
    Equal(count, d.Expenses.Count); Equal(true, d.Expenses.All(e => e.RecurrenceId is null && e.RecurrenceMonth is null)); BudgetRules.Validate(d);
});

// Dados de recorrência inconsistentes nunca são salvos nem importados.
Test("Invalid recurrence data is rejected", () =>
{
    BudgetData Fresh() { var d = Family(); d.Recurrences.Add(Rent(d)); RecurrenceEngine.Generate(d, today); return d; }
    var d = Fresh(); d.Recurrences[0].DayOfMonth = 0; Reject(() => BudgetRules.Validate(d));
    d = Fresh(); d.Recurrences[0].DayOfMonth = 32; Reject(() => BudgetRules.Validate(d));
    d = Fresh(); d.Recurrences[0].EndMonth = new DateOnly(2025, 12, 1); Reject(() => BudgetRules.Validate(d));
    d = Fresh(); d.Recurrences[0].StartMonth = new DateOnly(2026, 1, 15); Reject(() => BudgetRules.Validate(d));
    d = Fresh(); d.Recurrences[0].Participants = []; Reject(() => BudgetRules.Validate(d));
    d = Fresh(); d.Recurrences[0].Amount = 0; Reject(() => BudgetRules.Validate(d));
    d = Fresh(); d.Expenses[0].RecurrenceId = Guid.NewGuid(); Reject(() => BudgetRules.Validate(d));          // modelo inexistente
    d = Fresh(); d.Expenses[0].RecurrenceMonth = null; Reject(() => BudgetRules.Validate(d));                  // vínculo pela metade
    d = Fresh(); d.Expenses[1].RecurrenceMonth = d.Expenses[0].RecurrenceMonth; Reject(() => BudgetRules.Validate(d)); // mês repetido
    d = Fresh(); d.Expenses[0].InstallmentCount = 2; d.Expenses[0].InstallmentGroup = Guid.NewGuid(); Reject(() => BudgetRules.Validate(d)); // recorrente não parcela
});

// A recorrência sobrevive a gravar e ler (inclusive os meses ignorados).
Test("Recurrence persistence roundtrip", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid());
    try
    {
        using var store = new LocalStore(path); var d = Family(); d.Recurrences.Add(Rent(d)); RecurrenceEngine.Generate(d, today);
        RecurrenceEngine.RemoveExpense(d, d.Expenses[3].Id); store.Save(d); var back = store.Load();
        Equal(1, back.Recurrences.Count); Equal(22, back.Expenses.Count); Equal(1, back.Recurrences[0].SkippedMonths.Count); Equal(0, RecurrenceEngine.Generate(back, today));
    }
    finally { Directory.Delete(path, true); }
});

// ---- Quem pagou e acerto de contas ----
// Marca como paga por alguém; PaidBy nulo é "não informado".
Expense PaidByPerson(BudgetData d, decimal amount, Guid? payer, SplitMode split = SplitMode.AvailableIncome) { var e = Bill(d, amount, split); e.Paid = true; e.PaidBy = payer; d.Expenses.Add(e); return e; }
decimal Owes(List<Debt> debts, Guid debtor, Guid creditor) => debts.Where(x => x.DebtorId == debtor && x.CreditorId == creditor).Sum(x => x.Amount);

// Ana paga 800 e a divisão proporcional dá 600/200: Bruno deve 200 a Ana, e Ana não deve nada a si mesma.
Test("Payer is owed the others' shares", () =>
{
    var d = Family(); var (ana, bruno) = (d.People[0].Id, d.People[1].Id); PaidByPerson(d, 800, ana);
    var debts = SettlementEngine.Debts(d); Equal(1, debts.Count); Equal(200m, Owes(debts, bruno, ana));
});

// Despesas em aberto e pagas sem pagador informado não geram dívida (comportamento antigo preservado).
Test("Unpaid and unattributed payments create no debt", () =>
{
    var d = Family(); d.Expenses.Add(Bill(d, 800)); PaidByPerson(d, 500, null);
    Equal(0, SettlementEngine.Debts(d).Count);
});

// Quem paga uma despesa individual de outra pessoa passa a ter o valor inteiro a receber; a própria despesa individual não gera dívida.
Test("Paying someone else's individual expense creates a full debt", () =>
{
    var d = Family(); var (ana, bruno) = (d.People[0].Id, d.People[1].Id);
    var e = PaidByPerson(d, 100, ana); e.Split = SplitMode.Individual; e.Participants = [bruno]; // Bruno é o responsável, Ana pagou
    Equal(100m, Owes(SettlementEngine.Debts(d), bruno, ana));
    var own = PaidByPerson(d, 50, ana, SplitMode.Individual); // Ana paga a sua própria
    Equal(100m, Owes(SettlementEngine.Debts(d), bruno, ana)); Equal(1, SettlementEngine.Debts(d).Count);
});

// As dívidas de um par se compensam: 200 a favor de Ana menos 100 a favor de Bruno deixa Bruno devendo 100.
Test("Mutual debts net out within a pair", () =>
{
    var d = Family(); var (ana, bruno) = (d.People[0].Id, d.People[1].Id);
    PaidByPerson(d, 800, ana);                           // Bruno deve 200 a Ana
    PaidByPerson(d, 600, bruno, SplitMode.Equal);        // Ana deve 300 a Bruno
    var debts = SettlementEngine.Debts(d); Equal(1, debts.Count); Equal(100m, Owes(debts, ana, bruno));
});

// Um acerto abate a dívida; pagar a mais inverte o sentido; pagar exatamente quita.
Test("Settlements reduce, clear and flip the balance", () =>
{
    var d = Family(); var (ana, bruno) = (d.People[0].Id, d.People[1].Id); PaidByPerson(d, 800, ana);
    d.Settlements.Add(new Settlement { PayerId = bruno, ReceiverId = ana, Amount = 150 }); Equal(50m, Owes(SettlementEngine.Debts(d), bruno, ana));
    d.Settlements.Add(new Settlement { PayerId = bruno, ReceiverId = ana, Amount = 50 }); Equal(0, SettlementEngine.Debts(d).Count);
    d.Settlements.Add(new Settlement { PayerId = bruno, ReceiverId = ana, Amount = 30 }); Equal(30m, Owes(SettlementEngine.Debts(d), ana, bruno)); BudgetRules.Validate(d);
});

// Com três pessoas, cada par é calculado separadamente: ninguém ganha dívida com quem não pagou nada por ele.
Test("Three people settle pairwise", () =>
{
    var d = Family(); d.People.Add(new Person { Name = "Carla" }); var (ana, bruno, carla) = (d.People[0].Id, d.People[1].Id, d.People[2].Id);
    var e = Bill(d, 900, SplitMode.Equal); e.Participants = [ana, bruno, carla]; e.Paid = true; e.PaidBy = ana; d.Expenses.Add(e);
    var debts = SettlementEngine.Debts(d); Equal(2, debts.Count); Equal(300m, Owes(debts, bruno, ana)); Equal(300m, Owes(debts, carla, ana));
});

// Centavos não se perdem: 100,00 em três partes (33,34/33,33/33,33) soma exatamente o que o pagador adiantou pelos outros.
Test("Debts add up to the exact amount advanced for others", () =>
{
    var d = Family(); d.People.Add(new Person { Name = "Carla" }); var ids = d.People.Select(p => p.Id).ToList();
    var e = Bill(d, 100, SplitMode.Equal); e.Participants = ids; e.Paid = true; e.PaidBy = ids[0]; d.Expenses.Add(e);
    var share = FinanceEngine.Allocate(d, e).Amounts[ids[0]];
    Equal(100m - share, SettlementEngine.Debts(d).Sum(x => x.Amount));
});

// Quem pagou só vale em despesa paga e com pessoa existente; acertos precisam de duas pessoas diferentes e valor positivo.
Test("Invalid payer and settlement data is rejected", () =>
{
    var d = Family(); var (ana, bruno) = (d.People[0].Id, d.People[1].Id);
    var open = Bill(d, 100); open.PaidBy = ana; d.Expenses.Add(open); Reject(() => BudgetRules.Validate(d));
    d = Family(); PaidByPerson(d, 100, Guid.NewGuid()); Reject(() => BudgetRules.Validate(d));
    d = Family(); d.Settlements.Add(new Settlement { PayerId = ana, ReceiverId = ana, Amount = 10 }); Reject(() => BudgetRules.Validate(d));
    d = Family(); d.Settlements.Add(new Settlement { PayerId = ana, ReceiverId = bruno, Amount = 0 }); Reject(() => BudgetRules.Validate(d));
    d = Family(); d.Settlements.Add(new Settlement { PayerId = ana, ReceiverId = Guid.NewGuid(), Amount = 10 }); Reject(() => BudgetRules.Validate(d));
    d = Family(); d.Settlements.Add(new Settlement { PayerId = ana, ReceiverId = bruno, Amount = 10.005m }); Reject(() => BudgetRules.Validate(d));
});

// Quem pagou e os acertos sobrevivem a gravar e ler; um arquivo v2 antigo (sem esses campos) continua abrindo.
Test("Payer and settlements persist; older v2 files still open", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid());
    try
    {
        using var store = new LocalStore(path); var d = Family(); var (ana, bruno) = (d.People[0].Id, d.People[1].Id); PaidByPerson(d, 800, ana);
        d.Settlements.Add(new Settlement { PayerId = bruno, ReceiverId = ana, Amount = 150 }); store.Save(d);
        var back = store.Load(); Equal(ana, back.Expenses[0].PaidBy!.Value); Equal(1, back.Settlements.Count); Equal(50m, Owes(SettlementEngine.Debts(back), bruno, ana));
        // Fabrica um arquivo "v2 anterior": sem Settlements e sem PaidBy.
        var node = JsonNode.Parse(JsonSerializer.Serialize(d, LocalStore.JsonOptions))!; node.AsObject().Remove("Settlements"); foreach (var e in node["Expenses"]!.AsArray()) e!.AsObject().Remove("PaidBy");
        File.WriteAllText(store.FilePath, node.ToJsonString()); var old = store.Load(); Equal(0, old.Settlements.Count); Equal(true, old.Expenses[0].PaidBy is null); Equal(0, SettlementEngine.Debts(old).Count);
    }
    finally { Directory.Delete(path, true); }
});

// Um arquivo v1 que traga quem pagou ou acertos é adulterado: recusado.
Test("Version 1 file with payer data is rejected", () =>
{
    var d = Family(); PaidByPerson(d, 100, d.People[0].Id); var path = Path.GetTempFileName();
    try { var node = JsonNode.Parse(JsonSerializer.Serialize(d, LocalStore.JsonOptions))!; node["Version"] = 1; node.AsObject().Remove("Recurrences"); File.WriteAllText(path, node.ToJsonString()); Reject(() => LocalStore.Read(path)); }
    finally { File.Delete(path); }
});

// ---- Várias rendas por pessoa ----
// A renda do mês é a soma das linhas (salário + extra) e o rateio proporcional usa esse total.
Test("Income lines add up and drive the split", () =>
{
    var d = Family(0, 1000); d.Incomes.Add(new MonthlyIncome { PersonId = d.People[0].Id, Month = month, Amount = 1000, Label = "Salário" }); d.Incomes.Add(new MonthlyIncome { PersonId = d.People[0].Id, Month = month, Amount = 2000, Label = "Extra" });
    Equal(3000m, FinanceEngine.Income(d, d.People[0].Id, month)); Equal(3, FinanceEngine.IncomeLines(d, d.People[0].Id, month).Count); // a linha "Renda" de zero criada por Family + Salário + Extra
    Equal(600m, FinanceEngine.Allocate(d, Bill(d, 800)).Amounts[d.People[0].Id]); BudgetRules.Validate(d);
});

// Nomes repetidos, mais de 20 linhas, nome vazio e identificador repetido são recusados.
Test("Income line validation", () =>
{
    var d = Family(); d.Incomes.Add(new MonthlyIncome { PersonId = d.People[0].Id, Month = month, Amount = 10, Label = " salário" }); d.Incomes[0].Label = "Salário"; Reject(() => BudgetRules.Validate(d));
    d = Family(); d.Incomes[0].Label = ""; Reject(() => BudgetRules.Validate(d));
    d = Family(); d.Incomes.Add(d.Incomes[0] with { Label = "Outra" }); Reject(() => BudgetRules.Validate(d)); // mesmo Id
    d = Family(); for (var i = 0; i < 20; i++) d.Incomes.Add(new MonthlyIncome { PersonId = d.People[0].Id, Month = month, Amount = 1, Label = "Linha " + i }); Reject(() => BudgetRules.Validate(d)); // 21 linhas
});

// Um arquivo anterior (uma renda por mês, sem Id nem Label) abre: a renda vira "Renda" e ganha identificador.
Test("Older income records without id and label still open", () =>
{
    var d = Family(); var node = JsonNode.Parse(JsonSerializer.Serialize(d, LocalStore.JsonOptions))!; var path = Path.GetTempFileName();
    try
    {
        foreach (var i in node["Incomes"]!.AsArray()) { i!.AsObject().Remove("Id"); i.AsObject().Remove("Label"); }
        File.WriteAllText(path, node.ToJsonString()); var back = LocalStore.Read(path);
        Equal(true, back.Incomes.All(i => i.Label == "Renda" && i.Id != Guid.Empty)); Equal(2, back.Incomes.Select(i => i.Id).Distinct().Count()); Equal(3000m, FinanceEngine.Income(back, back.People[0].Id, month));
    }
    finally { File.Delete(path); }
});

// ---- Categorias personalizadas ----
// Adicionar, usar numa despesa e ver a categoria nas previsões; as padrão continuam disponíveis.
Test("Custom category can be added and used", () =>
{
    var d = Family(); CategoryEngine.Add(d, "  Pets "); Equal("Pets", d.CustomCategories[0]); Equal(9, BudgetRules.AllCategories(d).Count);
    var e = Bill(d, 90); e.Category = "Pets"; e.DueDate = month.AddMonths(1); d.Expenses.Add(e); BudgetRules.Validate(d);
    Equal(true, FinanceEngine.Forecasts(d, month.AddMonths(1)).Any(f => f.Category == "Pets" && f.Estimate == 90m));
});

// Nome vazio, longo, repetido (ignorando caixa), igual a uma padrão e acima de 40 categorias são recusados.
Test("Custom category names are validated", () =>
{
    var d = Family(); CategoryEngine.Add(d, "Pets");
    Reject(() => CategoryEngine.Add(d, "  ")); Reject(() => CategoryEngine.Add(d, new string('x', 31))); Reject(() => CategoryEngine.Add(d, "pets")); Reject(() => CategoryEngine.Add(d, "mercado")); Reject(() => CategoryEngine.Add(d, "Ab\nc"));
    for (var i = 1; i < CategoryEngine.MaxCustom; i++) CategoryEngine.Add(d, "Cat " + i); Reject(() => CategoryEngine.Add(d, "Uma a mais"));
    var bad = Family(); bad.CustomCategories = ["Moradia"]; Reject(() => BudgetRules.Validate(bad)); bad.CustomCategories = ["A", "a"]; Reject(() => BudgetRules.Validate(bad)); bad.CustomCategories = [" A"]; Reject(() => BudgetRules.Validate(bad));
    var unknown = Family(); unknown.Expenses.Add(Bill(unknown, 10)); unknown.Expenses[0].Category = "Inexistente"; Reject(() => BudgetRules.Validate(unknown));
});

// Renomear atualiza despesas e recorrências; as padrão não podem ser renomeadas nem excluídas.
Test("Renaming a category updates its expenses and recurrences", () =>
{
    var d = Family(); CategoryEngine.Add(d, "Pets"); var e = Bill(d, 90); e.Category = "Pets"; d.Expenses.Add(e);
    var r = Rent(d); r.Category = "Pets"; d.Recurrences.Add(r);
    CategoryEngine.Rename(d, "Pets", "Bichos"); Equal("Bichos", d.Expenses[0].Category); Equal("Bichos", d.Recurrences[0].Category); BudgetRules.Validate(d);
    CategoryEngine.Rename(d, "Bichos", "bichos"); Equal("bichos", d.CustomCategories[0]);
    Reject(() => CategoryEngine.Rename(d, "Mercado", "Feira")); Reject(() => CategoryEngine.Remove(d, "Mercado")); CategoryEngine.Add(d, "Outra"); Reject(() => CategoryEngine.Rename(d, "bichos", "outra"));
});

// Excluir só é possível sem uso; com uso, a mensagem explica o que fazer.
Test("A category in use cannot be removed", () =>
{
    var d = Family(); CategoryEngine.Add(d, "Pets"); var e = Bill(d, 90); e.Category = "Pets"; d.Expenses.Add(e);
    Reject(() => CategoryEngine.Remove(d, "Pets")); d.Expenses.Clear(); CategoryEngine.Remove(d, "Pets"); Equal(0, d.CustomCategories.Count);
});

// As categorias próprias sobrevivem a gravar e ler; arquivos sem o campo continuam abrindo.
Test("Custom categories persist; older files still open", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid());
    try
    {
        using var store = new LocalStore(path); var d = Family(); CategoryEngine.Add(d, "Pets"); store.Save(d); Equal("Pets", store.Load().CustomCategories[0]);
        var node = JsonNode.Parse(JsonSerializer.Serialize(d, LocalStore.JsonOptions))!; node.AsObject().Remove("CustomCategories"); File.WriteAllText(store.FilePath, node.ToJsonString()); Equal(0, store.Load().CustomCategories.Count);
    }
    finally { Directory.Delete(path, true); }
});

// ---- Metas por categoria ----
// Gasto, meta e nível: 80% é "perto", exatamente 100% ainda é "perto", acima de 100% é "acima".
Test("Goal progress levels", () =>
{
    var d = Family(); GoalEngine.Set(d, "Mercado", 1000);
    GoalProgress At(decimal spent) { d.Expenses.Clear(); if (spent > 0) { var e = Bill(d, spent); e.Category = "Mercado"; d.Expenses.Add(e); } return GoalEngine.Progress(d, month).Single(); }
    Equal(GoalLevel.Ok, At(0).Level); Equal(GoalLevel.Ok, At(799.99m).Level); Equal(GoalLevel.Near, At(800).Level); Equal(GoalLevel.Near, At(1000).Level); Equal(GoalLevel.Over, At(1000.01m).Level);
    Equal(0.5m, At(500).Ratio); Equal(500m, At(500).Spent);
});

// Só despesas do mês e da categoria contam (pagas ou não); definir de novo troca o valor e limpar remove.
Test("Goal counts the month and category only", () =>
{
    var d = Family(); GoalEngine.Set(d, "Mercado", 800); GoalEngine.Set(d, "Mercado", 600); Equal(1, d.Goals.Count); Equal(600m, d.Goals[0].Amount);
    var a = Bill(d, 100); a.Category = "Mercado"; var b = Bill(d, 50); b.Category = "Mercado"; b.Paid = true; var other = Bill(d, 70); other.Category = "Lazer"; var later = Bill(d, 90); later.Category = "Mercado"; later.DueDate = month.AddMonths(1);
    d.Expenses.AddRange([a, b, other, later]); Equal(150m, GoalEngine.Progress(d, month)[0].Spent);
    GoalEngine.Clear(d, "Mercado"); Equal(0, d.Goals.Count);
});

// Meta inválida (categoria inexistente, valor zero ou duplicada) é recusada; renomear acompanha e excluir a categoria remove a meta.
Test("Goal validation and category interplay", () =>
{
    var d = Family(); Reject(() => GoalEngine.Set(d, "Inexistente", 100)); Reject(() => GoalEngine.Set(d, "Mercado", 0));
    d.Goals = [new() { Category = "Mercado", Amount = 10 }, new() { Category = "MERCADO", Amount = 20 }]; Reject(() => BudgetRules.Validate(d));
    d = Family(); CategoryEngine.Add(d, "Pets"); GoalEngine.Set(d, "Pets", 200); CategoryEngine.Rename(d, "Pets", "Bichos"); Equal("Bichos", d.Goals[0].Category); BudgetRules.Validate(d);
    CategoryEngine.Remove(d, "Bichos"); Equal(0, d.Goals.Count);
});

// Metas persistem; arquivos anteriores sem o campo abrem; v1 com metas é recusado.
Test("Goals persist; older files open; v1 with goals rejected", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid());
    try
    {
        using var store = new LocalStore(path); var d = Family(); GoalEngine.Set(d, "Mercado", 800); store.Save(d); Equal(800m, store.Load().Goals[0].Amount);
        var node = JsonNode.Parse(JsonSerializer.Serialize(d, LocalStore.JsonOptions))!; node.AsObject().Remove("Goals"); File.WriteAllText(store.FilePath, node.ToJsonString()); Equal(0, store.Load().Goals.Count);
        node = JsonNode.Parse(JsonSerializer.Serialize(d, LocalStore.JsonOptions))!; node["Version"] = 1; node.AsObject().Remove("Recurrences"); File.WriteAllText(store.FilePath, node.ToJsonString()); Reject(() => store.Load());
    }
    finally { Directory.Delete(path, true); }
});

// ---- Busca em todos os meses ----
// Sem mês, procura em todos (mais recente primeiro); com mês, só naquele (por vencimento).
Test("Search across all months or one month", () =>
{
    var d = Family(); var a = Bill(d, 10); a.Title = "Mercado de outubro"; var b = Bill(d, 20); b.Title = "Mercado de setembro"; b.DueDate = month.AddMonths(-1); var c = Bill(d, 30); c.Title = "Cinema"; c.Category = "Lazer"; c.DueDate = month.AddMonths(2);
    d.Expenses.AddRange([b, a, c]);
    Equal(2, ExpenseSearch.Run(d, null, "mercado").Count); Equal("Mercado de outubro", ExpenseSearch.Run(d, null, "mercado")[0].Title); // mais recente primeiro
    Equal(1, ExpenseSearch.Run(d, month, "mercado").Count); Equal(3, ExpenseSearch.Run(d, null, "").Count); Equal(1, ExpenseSearch.Run(d, month, "").Count);
    Equal("Cinema", ExpenseSearch.Run(d, null, "")[0].Title);
});

// A busca ignora maiúsculas e acentos, procura também na categoria e combina com situação e categoria.
Test("Search ignores case and accents and combines filters", () =>
{
    var d = Family(); var a = Bill(d, 10); a.Title = "Consulta médica"; a.Category = "Saúde"; var b = Bill(d, 20); b.Title = "Farmácia"; b.Category = "Saúde"; b.Paid = true; var c = Bill(d, 30); c.Title = "Pão"; d.Expenses.AddRange([a, b, c]);
    Equal(1, ExpenseSearch.Run(d, null, "MEDICA").Count); Equal(2, ExpenseSearch.Run(d, null, "saude").Count); Equal(1, ExpenseSearch.Run(d, null, "pao").Count);
    Equal(1, ExpenseSearch.Run(d, null, "saude", paid: true).Count); Equal(1, ExpenseSearch.Run(d, null, "", paid: false, category: "Saúde").Count); Equal(0, ExpenseSearch.Run(d, null, "xyz").Count);
});

// ---- CSV: leitura, exportação e importação de extrato ----
// Campos entre aspas com separador, aspas duplicadas e quebra de linha; BOM, "sep=" do Excel e linhas vazias.
Test("CSV parser handles quotes, newlines, BOM and sep line", () =>
{
    var t = Csv.Parse("\uFEFFa;b;c\r\n1;\"x;y\";\"say \"\"hi\"\"\"\r\n\r\n2;\"two\nlines\";z\r\n");
    Equal(';', t.Delimiter); Equal(3, t.Rows.Count); Equal("x;y", t.Rows[1][1]); Equal("say \"hi\"", t.Rows[1][2]); Equal("two\nlines", t.Rows[2][1]);
    Equal(',', Csv.Parse("a,b,c\n1,2,3").Delimiter); Equal('\t', Csv.Parse("a\tb\n1\t2").Delimiter); Equal(';', Csv.Parse("sep=;\r\na;b\r\n1;2").Delimiter); Equal(2, Csv.Parse("sep=;\r\na;b\r\n1;2").Rows.Count);
    Equal(";", Csv.Parse("\"a,b\";\"c,d\"\n1;2").Delimiter.ToString()); // vírgulas dentro de aspas não decidem o separador
    Reject(() => Csv.Parse("a;\"nunca fecha\n1;2")); Reject(() => Csv.Parse("   \r\n  \r\n"));
});

// Valores em vários formatos de extrato; texto, mais de 2 casas e ambiguidades ruins são recusados.
Test("Statement amounts parse in common formats", () =>
{
    (string Text, decimal Value)[] good = [("1.234,56", 1234.56m), ("1234,56", 1234.56m), ("-1234.56", -1234.56m), ("1,234.56", 1234.56m), ("R$ 12,00", 12m), ("-R$ 12,00", -12m), ("(12,00)", -12m), ("12,00-", -12m), ("1.234", 1234m), ("12,5", 12.5m), ("1.234.567,89", 1234567.89m), ("0,99", 0.99m), ("+7,10", 7.10m), ("3", 3m)];
    foreach (var (text, value) in good) { Equal(true, StatementImport.TryParseAmount(text, out var parsed)); Equal(value, parsed); }
    foreach (var bad in new[] { "", "abc", "12,345,6x", "1,234567", "1.2.3,4,5", "--5", "R$", "12,3456", "-", "1e5" }) Equal(false, StatementImport.TryParseAmount(bad, out _));
});

// Datas comuns; a hora depois da data é ignorada; datas impossíveis são recusadas.
Test("Statement dates parse in common formats", () =>
{
    foreach (var text in new[] { "05/11/2026", "5/11/2026", "05/11/26", "2026-11-05", "05-11-2026", "05.11.2026", "2026/11/05", "2026-11-05 10:30:00", "2026-11-05T10:30:00", "05/11/2026 08:00" })
    { Equal(true, StatementImport.TryParseDate(text, out var d)); Equal(new DateOnly(2026, 11, 5), d); }
    foreach (var bad in new[] { "", "31/02/2026", "ontem", "13/13/2026", "2026" }) Equal(false, StatementImport.TryParseDate(bad, out _));
});

// A exportação usa ; e vírgula decimal, BOM na gravação, e neutraliza fórmulas (=, +, -, @) com apóstrofo.
Test("CSV export format and formula protection", () =>
{
    var d = Family(); var a = Bill(d, 1234.5m, SplitMode.Equal); a.Title = "=HYPERLINK(\"http://x\")"; a.Category = "Mercado"; a.Paid = true; a.PaidBy = d.People[0].Id; var b = Bill(d, 10); b.Title = "-cmd|calc; \"x\""; b.DueDate = month.AddDays(1); d.Expenses.AddRange([a, b]);
    var text = CsvExport.Expenses(d); var rows = Csv.Parse(text).Rows;
    Equal(3, rows.Count); Equal("Data", rows[0][0]); Equal("01/10/2026", rows[1][0]); Equal("1234,50", rows[1][3]); Equal("Pago", rows[1][4]); Equal("Ana", rows[1][5]); Equal("Em aberto", rows[2][4]);
    Equal(true, rows[1][1].StartsWith("'=")); Equal(true, rows[2][1].StartsWith("'-")); Equal("-cmd|calc; \"x\"", rows[2][1].TrimStart('\''));
    Equal("Ana / Bruno", rows[1][6]); Equal(true, rows[1][7].Contains("617,25"));
    Equal(2, CsvExport.Expenses(d, month.AddMonths(1)).Split("\r\n").Length); // só o cabeçalho (mais a quebra final)
    var path = Path.GetTempFileName(); try { CsvExport.Save(path, text); var raw = File.ReadAllBytes(path); Equal(true, raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF); } finally { File.Delete(path); }
});

// Células comuns não ganham aspas nem apóstrofo; as perigosas e as com separador sim.
Test("CSV cell escaping", () =>
{
    Equal("Mercado", Csv.Cell("Mercado")); Equal("\"a;b\"", Csv.Cell("a;b")); Equal("\"a\"\"b\"", Csv.Cell("a\"b")); Equal("'=1+1", Csv.Cell("=1+1")); Equal("'@x", Csv.Cell("@x")); Equal("'+1", Csv.Cell("+1")); Equal("\" x\"", Csv.Cell(" x"));
});

// O mapeamento é sugerido pelos nomes do cabeçalho ou, sem cabeçalho, pelo conteúdo das colunas.
Test("Statement mapping is guessed from headers and from content", () =>
{
    var withHeader = StatementImport.Guess(Csv.Parse("Data;Histórico;Valor;Saldo\n05/11/2026;PIX MERCADO;-45,90;1000,00"));
    Equal(0, withHeader.DateColumn); Equal(1, withHeader.DescriptionColumn); Equal(2, withHeader.AmountColumn); Equal(true, withHeader.HasHeader);
    var noHeader = StatementImport.Guess(Csv.Parse("05/11/2026;PADARIA;-12,00\n06/11/2026;FARMACIA;-30,50\n07/11/2026;SALARIO;3000,00"));
    Equal(0, noHeader.DateColumn); Equal(1, noHeader.DescriptionColumn); Equal(2, noHeader.AmountColumn); Equal(false, noHeader.HasHeader);
    var english = StatementImport.Guess(Csv.Parse("Date,Description,Amount\n2026-11-05,Coffee,-3.50"));
    Equal(0, english.DateColumn); Equal(1, english.DescriptionColumn); Equal(2, english.AmountColumn);
});

// Negativos são despesas pagas em "Outros", sem pagador; entradas, zero e linhas ilegíveis ficam de fora (com o número da linha).
Test("Statement plan creates paid expenses and reports the rest", () =>
{
    var d = Family(); var table = Csv.Parse("Data;Descrição;Valor\n05/11/2026;PIX  MERCADO ;-45,90\n06/11/2026;SALÁRIO;3000,00\n07/11/2026;ZERO;0,00\nlixo;X;-1,00\n08/11/2026;Y;abc\n09/11/2026;;-5,00");
    var plan = StatementImport.Plan(d, table, StatementImport.Guess(table), SplitMode.Individual, [d.People[0].Id]);
    Equal(2, plan.Expenses.Count); Equal(2, plan.IgnoredEntries); Equal(2, plan.ErrorCount); Equal("Linha 5: data ilegível", plan.Errors[0]); Equal("Linha 6: valor ilegível", plan.Errors[1]);
    Equal("PIX MERCADO", plan.Expenses[0].Title); Equal(45.90m, plan.Expenses[0].Amount); Equal("Outros", plan.Expenses[0].Category); Equal(true, plan.Expenses[0].Paid); Equal(true, plan.Expenses[0].PaidBy is null); Equal("Sem descrição", plan.Expenses[1].Title);
    d.Expenses.AddRange(plan.Expenses); BudgetRules.Validate(d);
});

// Fatura de cartão: positivos são compras e negativos (estornos) são ignorados.
Test("Statement plan supports card statements (positive is expense)", () =>
{
    var d = Family(); var table = Csv.Parse("Data;Descrição;Valor\n05/11/2026;LOJA;100,00\n06/11/2026;ESTORNO;-20,00");
    var plan = StatementImport.Plan(d, table, StatementImport.Guess(table) with { Sign = AmountSign.PositiveIsExpense }, SplitMode.Equal, d.People.Select(p => p.Id).ToList());
    Equal(1, plan.Expenses.Count); Equal(100m, plan.Expenses[0].Amount); Equal(1, plan.IgnoredEntries); Equal(2, plan.Expenses[0].Participants.Count);
});

// Importar o mesmo extrato de novo não duplica nada; duas linhas iguais no arquivo valem como duas compras reais.
Test("Statement re-import skips what already exists", () =>
{
    var d = Family(); var table = Csv.Parse("Data;Descrição;Valor\n05/11/2026;CAFE;-5,00\n05/11/2026;CAFE;-5,00\n06/11/2026;PAO;-7,00");
    var map = StatementImport.Guess(table); var me = new List<Guid> { d.People[0].Id };
    var first = StatementImport.Plan(d, table, map, SplitMode.Individual, me); Equal(3, first.Expenses.Count); Equal(0, first.Duplicates);
    d.Expenses.AddRange(first.Expenses); var second = StatementImport.Plan(d, table, map, SplitMode.Individual, me); Equal(0, second.Expenses.Count); Equal(3, second.Duplicates);
    d.Expenses.RemoveAt(0); Equal(1, StatementImport.Plan(d, table, map, SplitMode.Individual, me).Expenses.Count); // faltava uma: só ela volta
});

// Responsável inválido e arquivos acima dos limites são recusados; a decodificação cai para Latin-1 quando não é UTF-8.
Test("Statement import limits and encoding", () =>
{
    var d = Family(); var table = Csv.Parse("Data;Descrição;Valor\n05/11/2026;X;-1,00");
    Reject(() => StatementImport.Plan(d, table, StatementImport.Guess(table), SplitMode.Individual, [])); Reject(() => StatementImport.Plan(d, table, StatementImport.Guess(table), SplitMode.Individual, d.People.Select(p => p.Id).ToList())); Reject(() => StatementImport.Plan(d, table, StatementImport.Guess(table), SplitMode.Equal, [Guid.NewGuid()]));
    Reject(() => Csv.Parse("a\n" + string.Concat(Enumerable.Repeat("1\n", Csv.MaxRows + 1))));
    var path = Path.GetTempFileName();
    try
    {
        File.WriteAllBytes(path, System.Text.Encoding.Latin1.GetBytes("Data;Descrição;Valor\n05/11/2026;Farmácia;-1,00")); Equal(true, StatementImport.ReadFile(path).Contains("Farmácia"));
        File.WriteAllBytes(path, new System.Text.UTF8Encoding(true).GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes("Descrição")).ToArray()); Equal(true, StatementImport.ReadFile(path).Contains("Descrição"));
        using (var stream = File.OpenWrite(path)) stream.SetLength(StatementImport.MaxBytes + 1); Reject(() => StatementImport.ReadFile(path));
    }
    finally { File.Delete(path); }
});

// ---- Windows e Linux ----
// A pasta de dados padrão fica na pasta local do usuário em qualquer sistema (%LOCALAPPDATA% no Windows, ~/.local/share no Linux).
Test("Default data folder is per-user and portable", () =>
{
    var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BankManager");
    Equal(expected, LocalStore.DefaultDirectory(null)); Equal(expected, LocalStore.DefaultDirectory("  ")); Equal("/custom", LocalStore.DefaultDirectory("/custom"));
    Equal(true, Path.IsPathRooted(expected) && !expected.StartsWith(AppContext.BaseDirectory)); // nunca dentro da pasta do programa: atualizar ou desinstalar não toca nos dados
});

// Pastas com espaços e acentos (comuns em nomes de usuário do Windows) funcionam para dados, configurações e exportação.
Test("Data folders with spaces and accents work", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "bankmanager test " + Guid.NewGuid() + " João çã");
    try
    {
        using var store = new LocalStore(path); var d = Family(); d.Expenses.Add(Bill(d, 800)); store.Save(d); SettingsStore.Save(path, new AppSettings { Theme = ThemePreference.Dark });
        Equal(800m, store.Load().Expenses[0].Amount); Equal(ThemePreference.Dark, SettingsStore.Load(path).Theme);
        var csv = Path.Combine(path, "exportação.csv"); CsvExport.Save(csv, CsvExport.Expenses(d)); Equal(true, StatementImport.ReadFile(csv).Contains("Descrição"));
    }
    finally { Directory.Delete(path, true); }
});

// Gráfico da visão geral: seis meses terminando no selecionado, do mais antigo ao mais recente, com zero nos meses vazios.
Test("Monthly totals cover the last N months including empty ones", () =>
{
    var d = Family(); var paid = Bill(d, 300); paid.Paid = true; d.Expenses.Add(paid); d.Expenses.Add(Bill(d, 200.50m));
    var old = Bill(d, 90); old.DueDate = month.AddMonths(-2).AddDays(14); d.Expenses.Add(old);
    var outside = Bill(d, 999); outside.DueDate = month.AddMonths(-6); d.Expenses.Add(outside); var future = Bill(d, 50); future.DueDate = month.AddMonths(1); d.Expenses.Add(future);
    var totals = FinanceEngine.MonthlyTotals(d, month.AddDays(20), 6);
    Equal(6, totals.Count); Equal(month.AddMonths(-5), totals[0].Month); Equal(month, totals[5].Month);
    Equal(500.50m, totals[5].Total); Equal(90m, totals[3].Total); Equal(0m, totals[0].Total); Equal(590.50m, totals.Sum(t => t.Total));
    Equal(1, FinanceEngine.MonthlyTotals(new BudgetData(), month, 1).Count);
    try { FinanceEngine.MonthlyTotals(d, month, 0); throw new Exception("Expected rejection"); } catch (ArgumentOutOfRangeException) { }
});

// Últimas movimentações: rendas do mês (dia 1) e despesas pagas do mês, mais recentes primeiro, com limite.
Test("Recent movements list incomes and paid expenses, newest first", () =>
{
    var d = Family(3000, 0); d.Incomes.Add(new() { PersonId = d.People[0].Id, Month = month, Amount = 500, Label = "Extra" });
    d.Incomes.Add(new() { PersonId = d.People[0].Id, Month = month.AddMonths(1), Amount = 7000 });
    Expense Paid(string title, int day, decimal amount, bool paid = true) { var e = Bill(d, amount); e.Title = title; e.DueDate = month.AddDays(day - 1); e.Paid = paid; d.Expenses.Add(e); return e; }
    Paid("Aluguel", 5, 1800); Paid("Energia", 12, 214.90m, paid: false); Paid("Internet", 12, 119.90m); Paid("Água", 12, 80); Paid("Dia um", 1, 10);
    var other = Paid("Outro mês", 3, 40); other.DueDate = month.AddMonths(-1);
    var all = FinanceEngine.RecentMovements(d, month, 10);
    // Dia 12 (Água antes de Internet, pela descrição), dia 5 e, no dia 1, a despesa antes das rendas na ordem de cadastro.
    Equal("Água,Internet,Aluguel,Dia um,Renda,Extra", string.Join(",", all.Select(m => m.Title)));
    Equal(true, all[4].IsIncome && all[4].Date == month && all[4].Category == "Renda de Ana" && all[4].Amount == 3000m); Equal(false, all[0].IsIncome);
    Equal(2, FinanceEngine.RecentMovements(d, month, 2).Count); Equal(0, FinanceEngine.RecentMovements(new BudgetData(), month).Count);
    // Mesma entrada, mesma saída: a ordem não muda entre chamadas.
    Equal(true, all.SequenceEqual(FinanceEngine.RecentMovements(d, month, 10)));
});

// Arquivos REAIS gravados pelas versões anteriores (pasta Fixtures): o v1 pelo código da branch main, o v2 pelo BankManager 2.1.
// Cada um abre sem perda, passa para a versão 3 só em memória e, ao salvar, o arquivo original vira a cópia anterior, byte a byte.
Test("Real version 1 and 2 files migrate to version 3 in a chain", () =>
{
    foreach (var (name, version) in new[] { ("financas-v1.json", 1), ("financas-v2.json", 2) })
    {
        var path = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid()); Directory.CreateDirectory(path);
        try
        {
            var original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)); File.WriteAllBytes(Path.Combine(path, "financas.json"), original);
            var before = JsonNode.Parse(original)!; Equal(version, (int)before["Version"]!);
            using var store = new LocalStore(path); var loaded = store.Load();
            Equal(3, loaded.Version); Equal(2, loaded.People.Count); Equal(before["Expenses"]!.AsArray().Count, loaded.Expenses.Count); Equal(before["Incomes"]!.AsArray().Count, loaded.Incomes.Count);
            Equal(before["Expenses"]!.AsArray().Sum(e => (decimal)e!["Amount"]!), loaded.Expenses.Sum(e => e.Amount));
            // Ler não altera o arquivo em disco.
            Equal(true, original.SequenceEqual(File.ReadAllBytes(store.FilePath)));
            // Despesas pagas antes da versão 3 ficam sem data de pagamento: a referência é o vencimento.
            var rent = loaded.Expenses.Single(e => e.Title == "Aluguel"); Equal(true, rent.Paid && rent.PaidDate is null && rent.OriginId is null); Equal(rent.DueDate, rent.ReferenceDate);
            Equal(3, loaded.Expenses.Count(e => e.Title == "Notebook" && e.InstallmentCount == 3)); Equal(2m, loaded.Expenses.Single(e => e.Title == "Mercado").Items[0].Quantity);
            if (version == 2)
            {
                Equal(true, rent.PaidBy == loaded.People[0].Id && loaded.CustomCategories.SequenceEqual(["Pets"]) && loaded.Goals.Single().Amount == 800m && loaded.Settlements.Single().Amount == 100m);
                Equal(1, loaded.Recurrences.Count); Equal(true, loaded.Expenses.Count(e => e.RecurrenceId is not null) > 1); Equal(3500m, FinanceEngine.Income(loaded, loaded.People[0].Id, month));
            }
            else Equal(3000m, FinanceEngine.Income(loaded, loaded.People[0].Id, month));
            store.Save(loaded);
            Equal(true, original.SequenceEqual(File.ReadAllBytes(store.BackupPath))); Equal(3, (int)JsonNode.Parse(File.ReadAllText(store.FilePath))!["Version"]!);
            Equal(loaded.Expenses.Sum(e => e.Amount), store.Load().Expenses.Sum(e => e.Amount));
            Equal(0, Directory.GetFiles(path, "*.tmp").Length);
        }
        finally { Directory.Delete(path, true); }
    }
});

// Um arquivo que diz ser v2 (ou v1) mas traz data de pagamento está adulterado; uma versão maior que a do aplicativo é recusada.
Test("Older versions with version 3 data and newer versions are rejected", () =>
{
    var path = Path.GetTempFileName();
    try
    {
        var d = Family(); var bill = Bill(d, 800); bill.Paid = true; bill.PaidDate = month.AddDays(3); d.Expenses.Add(bill);
        foreach (var version in new[] { 1, 2, 4 })
        {
            var node = JsonNode.Parse(JsonSerializer.Serialize(d, LocalStore.JsonOptions))!; node["Version"] = version; File.WriteAllText(path, node.ToJsonString()); Reject(() => LocalStore.Read(path));
        }
        // O mesmo conteúdo na versão 3 é válido e a data de pagamento sobrevive à gravação.
        LocalStore.Export(path, d); Equal(month.AddDays(3), LocalStore.Read(path).Expenses[0].PaidDate);
    }
    finally { File.Delete(path); }
});

// A data de pagamento só existe em despesa paga, dentro do intervalo de datas aceito; parcelas novas nascem sem ela.
Test("Paid date requires a paid expense and a valid date", () =>
{
    var d = Family(); var bill = Bill(d, 100); bill.PaidDate = month; d.Expenses.Add(bill); Reject(() => BudgetRules.Validate(d));
    bill.Paid = true; BudgetRules.Validate(d);
    bill.PaidDate = new DateOnly(1999, 12, 31); Reject(() => BudgetRules.Validate(d));
    bill.PaidDate = null; BudgetRules.Validate(d); Equal(bill.DueDate, bill.ReferenceDate);
    var source = Bill(d, 300); source.Paid = true; source.PaidDate = month; source.PaidBy = d.People[0].Id;
    Equal(true, BudgetRules.Installments(source, 3).All(e => !e.Paid && e.PaidDate is null && e.PaidBy is null)); Equal(month, BudgetRules.Installments(source, 1)[0].PaidDate);
});

// Movimentações usam a data do pagamento: a despesa aparece no mês em que foi paga, não no do vencimento.
Test("Recent movements follow the paid date into its month", () =>
{
    var d = Family(0, 0);
    Expense Paid(string title, DateOnly due, DateOnly? paidOn) { var e = Bill(d, 100); e.Title = title; e.DueDate = due; e.Paid = true; e.PaidDate = paidOn; d.Expenses.Add(e); return e; }
    Paid("Venceu em outubro, paga em novembro", month.AddDays(27), month.AddMonths(1).AddDays(2));
    Paid("Vence em novembro, paga adiantada", month.AddMonths(1).AddDays(9), month.AddDays(29));
    Paid("Sem data de pagamento", month.AddDays(4), null); Paid("Paga no dia", month.AddDays(9), month.AddDays(9));
    Equal("Vence em novembro, paga adiantada,Paga no dia,Sem data de pagamento", string.Join(",", FinanceEngine.RecentMovements(d, month).Select(m => m.Title)));
    Equal(month.AddDays(29), FinanceEngine.RecentMovements(d, month)[0].Date); Equal(month.AddDays(4), FinanceEngine.RecentMovements(d, month)[2].Date);
    Equal("Venceu em outubro, paga em novembro", string.Join(",", FinanceEngine.RecentMovements(d, month.AddMonths(1)).Select(m => m.Title)));
    // O total do mês (gráfico e saldo) continua seguindo o vencimento.
    Equal(300m, FinanceEngine.MonthlyTotals(d, month, 1)[0].Total);
});

// Extrato importado: a data da própria linha vira a data de pagamento; a planilha exportada traz a coluna "Pago em".
Test("Statement import sets the paid date and CSV export shows it", () =>
{
    var d = Family(); var table = Csv.Parse("Data;Histórico;Valor\r\n02/10/2026;PADARIA;-12,50\r\n");
    var plan = StatementImport.Plan(d, table, new ImportMapping(0, 1, 2, true, AmountSign.NegativeIsExpense), SplitMode.Individual, [d.People[0].Id]);
    Equal(new DateOnly(2026, 10, 2), plan.Expenses.Single().PaidDate); d.Expenses.AddRange(plan.Expenses); BudgetRules.Validate(d);
    var rows = Csv.Parse(CsvExport.Expenses(d)).Rows; Equal("Pago em", rows[0][^1]); Equal("02/10/2026", rows[1][^1]);
});

// ===== Compartilhamento de despesas =====
// Base de quem envia: Ana e Bruno, com renda, meta, acerto, uma compra parcelada, uma categoria própria e despesas pagas.
BudgetData Sender()
{
    var d = Family(); d.CustomCategories.Add("Pets"); GoalEngine.Set(d, "Mercado", 900);
    var market = Bill(d, 800); market.Paid = true; market.PaidBy = d.People[0].Id; market.PaidDate = month.AddDays(2); d.Expenses.Add(market);
    d.Expenses.Add(new Expense { Title = "Ração", Category = "Pets", Amount = 89.90m, DueDate = month.AddDays(8), Split = SplitMode.Individual, Participants = [d.People[1].Id] });
    d.Expenses.Add(new Expense { Title = "Feira", Category = "Mercado", Amount = 51m, DueDate = month.AddDays(9), Split = SplitMode.Equal, Participants = d.People.Select(p => p.Id).ToList(), Items = [new ExpenseItem { Name = "Arroz", Quantity = 2, UnitPrice = 25.50m }] });
    d.Expenses.AddRange(BudgetRules.Installments(new Expense { Title = "Notebook", Category = "Educação", Amount = 1000m, DueDate = month.AddDays(14), Split = SplitMode.Equal, Participants = d.People.Select(p => p.Id).ToList() }, 3));
    d.Expenses.Add(new Expense { Title = "Segredo", Category = "Outros", Amount = 999m, DueDate = month, Split = SplitMode.Individual, Participants = [d.People[0].Id] });
    d.Settlements.Add(new Settlement { PayerId = d.People[1].Id, ReceiverId = d.People[0].Id, Amount = 50, Date = month.AddDays(5) });
    BudgetRules.Validate(d); return d;
}
// Base de quem recebe: Carla, com renda, meta, uma despesa e um acerto próprios, que nunca podem ser tocados.
BudgetData Receiver()
{
    var d = new BudgetData { People = [new() { Name = "Carla" }, new() { Name = "Érica" }] };
    d.Incomes.Add(new() { PersonId = d.People[0].Id, Month = month, Amount = 2500 }); GoalEngine.Set(d, "Lazer", 200);
    d.Expenses.Add(new Expense { Title = "Cinema", Category = "Lazer", Amount = 60, DueDate = month.AddDays(3), Split = SplitMode.Individual, Participants = [d.People[0].Id] });
    d.Settlements.Add(new Settlement { PayerId = d.People[1].Id, ReceiverId = d.People[0].Id, Amount = 10, Date = month });
    BudgetRules.Validate(d); return d;
}
string Snapshot(BudgetData d) => JsonSerializer.Serialize(d, LocalStore.JsonOptions);

// O arquivo leva só as despesas escolhidas, com pessoas por nome; nunca rendas, metas, acertos ou despesas não selecionadas.
Test("Share file contains only the selected expenses", () =>
{
    var d = Sender(); var chosen = d.Expenses.Where(e => e.Title is "Mercado" or "Ração").Select(e => e.Id).ToList();
    var file = ExpenseShare.Build(d, chosen, includePayment: false);
    Equal("share", file.Kind); Equal("BankManager", file.App); Equal(2, file.Expenses.Count); Equal(true, file.People.SequenceEqual(["Ana", "Bruno"])); Equal(true, file.CustomCategories.SequenceEqual(["Pets"]));
    // Sem a opção, tudo sai em aberto, sem data nem pagador.
    Equal(true, file.Expenses.All(e => !e.Paid && e.PaidDate is null && e.PaidBy is null));
    var json = JsonSerializer.Serialize(file, LocalStore.JsonOptions);
    foreach (var forbidden in new[] { "Segredo", "Notebook", "Incomes", "Goals", "Settlements", "3000", "Recurrences", d.People[0].Id.ToString() }) Equal(false, json.Contains(forbidden));
    // Com a opção, a situação, a data e quem pagou acompanham; só quem é citado entra na lista de pessoas.
    var paid = ExpenseShare.Build(d, [d.Expenses.First(e => e.Title == "Mercado").Id], includePayment: true).Expenses.Single();
    Equal(true, paid.Paid && paid.PaidBy == "Ana" && paid.PaidDate == month.AddDays(2));
    Equal(true, ExpenseShare.Build(d, [d.Expenses.First(e => e.Title == "Ração").Id], false).People.SequenceEqual(["Bruno"]));
    Reject(() => ExpenseShare.Build(d, [], false)); Reject(() => ExpenseShare.Build(d, [Guid.NewGuid()], false));
});

// Importar ADICIONA: mapeia pessoas (existente, nova, ignorada), cria categoria, e não toca em nada que já existia.
Test("Import adds expenses with mapped people and categories, touching nothing else", () =>
{
    var sender = Sender(); var receiver = Receiver(); var before = Snapshot(receiver);
    var file = ExpenseShare.Build(sender, sender.Expenses.Where(e => e.Title is "Mercado" or "Ração" or "Feira").Select(e => e.Id).ToList(), includePayment: true);
    var suggestion = ExpenseShare.Suggest(receiver, file);
    Equal(PersonAction.Create, suggestion.People["Ana"].Action); Equal(true, suggestion.CreateCategories["Pets"]);
    // Ana → Carla (existente), Bruno → cadastro novo.
    var mapping = suggestion with { People = new() { ["Ana"] = new(PersonAction.Existing, receiver.People[0].Id), ["Bruno"] = new(PersonAction.Create) } };
    var (next, result) = ExpenseShare.Import(receiver, file, mapping);
    Equal(before, Snapshot(receiver)); // a base original não muda: a importação trabalha em uma cópia
    Equal(3, result.Added.Count); Equal(0, result.Duplicates); Equal(0, result.Errors.Count); Equal(true, result.NewPeople.SequenceEqual(["Bruno"])); Equal(true, result.NewCategories.SequenceEqual(["Pets"]));
    Equal(4, next.Expenses.Count); Equal(3, next.People.Count); Equal(true, next.CustomCategories.SequenceEqual(["Pets"]));
    // Tudo o que existia continua igual: rendas, metas, acertos e a despesa antiga.
    Equal(Snapshot(new BudgetData { Incomes = receiver.Incomes, Goals = receiver.Goals, Settlements = receiver.Settlements, Expenses = [receiver.Expenses[0]] }), Snapshot(new BudgetData { Incomes = next.Incomes, Goals = next.Goals, Settlements = next.Settlements, Expenses = [next.Expenses[0]] }));
    var market = next.Expenses.Single(e => e.Title == "Mercado"); var original = sender.Expenses.First(e => e.Title == "Mercado");
    Equal(true, market.Id != original.Id && market.OriginId == original.Id && market.Paid && market.PaidBy == receiver.People[0].Id && market.PaidDate == month.AddDays(2) && market.Participants.Count == 2);
    Equal(1, result.Proportional); Equal(800m, FinanceEngine.Allocate(next, market).Amounts.Values.Sum()); // o rateio fecha com as rendas locais
    Equal("Pets", next.Expenses.Single(e => e.Title == "Ração").Category); Equal(2m, next.Expenses.Single(e => e.Title == "Feira").Items[0].Quantity);
    // Nomes iguais sem acento ou maiúsculas são sugeridos como a mesma pessoa.
    var accents = ExpenseShare.Build(sender, [sender.Expenses.First(e => e.Title == "Ração").Id], false) with { People = ["erica"] }; accents.Expenses[0].Participants = ["erica"];
    Equal(receiver.People[1].Id, ExpenseShare.Suggest(receiver, accents).People["erica"].PersonId);
});

// Pessoa ignorada sai da despesa; se ninguém sobra, é preciso escolher quem assume. Categoria não criada vira "Outros"; opção de divisão igual.
Test("Ignored people, fallback, Outros and equal split options", () =>
{
    var sender = Sender(); var receiver = Receiver(); var before = Snapshot(receiver);
    var file = ExpenseShare.Build(sender, sender.Expenses.Where(e => e.Title is "Mercado" or "Ração").Select(e => e.Id).ToList(), includePayment: true);
    var mapping = new ShareMapping { People = new() { ["Ana"] = new(PersonAction.Existing, receiver.People[0].Id), ["Bruno"] = new(PersonAction.Ignore) }, EqualInsteadOfProportional = true };
    // "Ração" era só do Bruno: sem escolher quem assume, nada é importado e a base fica intacta.
    Reject(() => ExpenseShare.Import(receiver, file, mapping)); Equal(before, Snapshot(receiver));
    var (next, result) = ExpenseShare.Import(receiver, file, mapping with { Fallback = receiver.People[1].Id });
    Equal(2, result.Added.Count); Equal(0, result.NewPeople.Count); Equal(0, result.NewCategories.Count); Equal(2, next.People.Count);
    var pet = next.Expenses.Single(e => e.Title == "Ração"); Equal("Outros", pet.Category); Equal(true, pet.Participants.SequenceEqual([receiver.People[1].Id]) && pet.Split == SplitMode.Individual);
    var market = next.Expenses.Single(e => e.Title == "Mercado"); Equal(true, market.Split == SplitMode.Equal && market.Participants.SequenceEqual([receiver.People[0].Id])); Equal(0, result.Proportional);
    // Escolha ausente para um nome é um erro claro, não um palpite.
    Reject(() => ExpenseShare.Import(receiver, file, new ShareMapping { People = new() { ["Ana"] = new(PersonAction.Create) } }));
    // Limite de 40 categorias próprias: criar além dele falha por inteiro; mandar para "Outros" funciona.
    var full = Receiver(); for (var i = 0; i < CategoryEngine.MaxCustom; i++) full.CustomCategories.Add("Cat " + i);
    Equal(false, ExpenseShare.Suggest(full, file).CreateCategories["Pets"]);
    var create = new ShareMapping { People = new() { ["Ana"] = new(PersonAction.Create), ["Bruno"] = new(PersonAction.Create) }, CreateCategories = new() { ["Pets"] = true } };
    Reject(() => ExpenseShare.Import(full, file, create)); Equal(2, ExpenseShare.Import(full, file, create with { CreateCategories = [] }).Result.Added.Count);
});

// Importar o mesmo arquivo duas vezes não duplica; "importar mesmo assim" cria cópias com novo Id; parcelas mantêm o vínculo.
Test("Duplicates are skipped by origin id and installments stay linked", () =>
{
    var sender = Sender(); var receiver = Receiver();
    var file = ExpenseShare.Build(sender, sender.Expenses.Where(e => e.Title is "Notebook" or "Feira").Select(e => e.Id).ToList(), includePayment: false);
    var mapping = ExpenseShare.Suggest(receiver, file);
    var (first, added) = ExpenseShare.Import(receiver, file, mapping); Equal(4, added.Added.Count);
    var parts = first.Expenses.Where(e => e.Title == "Notebook").OrderBy(e => e.InstallmentNumber).ToList();
    Equal(true, parts.Count == 3 && parts.Select(e => e.InstallmentGroup).Distinct().Count() == 1 && parts[0].InstallmentGroup is not null && parts[0].InstallmentGroup != sender.Expenses.First(e => e.Title == "Notebook").InstallmentGroup);
    Equal(true, parts.Select(e => e.InstallmentNumber).SequenceEqual([1, 2, 3]) && parts.Sum(e => e.Amount) == 1000m && first.Expenses.Single(e => e.Title == "Feira").InstallmentGroup is null);
    // Segunda importação: tudo duplicado, nada adicionado. As pessoas criadas na primeira vez são reaproveitadas.
    var (second, again) = ExpenseShare.Import(first, file, ExpenseShare.Suggest(first, file)); Equal(0, again.Added.Count); Equal(4, again.Duplicates); Equal(Snapshot(first), Snapshot(second));
    // Importar mesmo assim: novas despesas, novos Ids, mesma origem; uma terceira tentativa continua reconhecendo as duplicadas.
    var (forced, copy) = ExpenseShare.Import(first, file, ExpenseShare.Suggest(first, file) with { ImportDuplicates = true });
    Equal(4, copy.Added.Count); Equal(first.Expenses.Count + 4, forced.Expenses.Count); Equal(forced.Expenses.Count, forced.Expenses.Select(e => e.Id).Distinct().Count());
    Equal(4, ExpenseShare.Import(forced, file, ExpenseShare.Suggest(forced, file)).Result.Duplicates);
    // Quem enviou importa o próprio arquivo: as despesas já existem aqui pelo Id.
    Equal(4, ExpenseShare.Import(sender, file, ExpenseShare.Suggest(sender, file)).Result.Duplicates);
    // Reexportar uma despesa importada preserva a origem inicial.
    Equal(sender.Expenses.First(e => e.Title == "Feira").Id, ExpenseShare.Build(first, [first.Expenses.Single(e => e.Title == "Feira").Id], false).Expenses[0].OriginId);
});

// Erros por despesa: a linha ruim é apontada e pulada, as boas entram; pessoas criadas só para a linha ruim são desfeitas.
Test("Per-expense errors are reported and skipped", () =>
{
    var sender = Sender(); var receiver = Receiver();
    var file = ExpenseShare.Build(sender, sender.Expenses.Where(e => e.Title is "Mercado" or "Ração").Select(e => e.Id).ToList(), false);
    file.Expenses.Single(e => e.Title == "Ração").Amount = -5; // arquivo adulterado depois de gerado
    var (next, result) = ExpenseShare.Import(receiver, file, ExpenseShare.Suggest(receiver, file));
    Equal(1, result.Added.Count); Equal(1, result.Errors.Count); Equal(true, result.Errors[0].Contains("Ração")); Equal(0, result.NewCategories.Count); Equal(0, next.CustomCategories.Count);
    Equal(true, result.NewPeople.OrderBy(n => n).SequenceEqual(["Ana", "Bruno"])); // os dois participam de "Mercado"
    file.Expenses.Single(e => e.Title == "Mercado").DueDate = new DateOnly(1800, 1, 1);
    var none = ExpenseShare.Import(receiver, file, ExpenseShare.Suggest(receiver, file)); Equal(0, none.Result.Added.Count); Equal(2, none.Result.Errors.Count); Equal(2, none.Next.People.Count);
});

// A prévia recebe uma linha por despesa do arquivo; "importar mesmo assim" vale por despesa; a versão do app viaja no arquivo.
Test("Preview lines, per-expense import anyway and app version", () =>
{
    var sender = Sender(); var receiver = Receiver();
    var file = ExpenseShare.Build(sender, sender.Expenses.Where(e => e.Title is "Mercado" or "Ração" or "Feira").Select(e => e.Id).ToList(), false, "2.2");
    Equal("2.2", file.AppVersion); file.AppVersion = "2.2<script>"; Reject(() => ExpenseShare.Validate(file)); file.AppVersion = "2.2";
    var (first, _) = ExpenseShare.Import(receiver, file, ExpenseShare.Suggest(receiver, file));
    var feira = file.Expenses.Single(e => e.Title == "Feira").OriginId;
    // Tudo já existe: três linhas puladas, na ordem do arquivo.
    var all = ExpenseShare.Import(first, file, ExpenseShare.Suggest(first, file)).Result;
    Equal(true, all.Lines.Count == 3 && all.Lines.All(l => l.Status == ShareLineStatus.Skipped && l.Existing) && all.Lines.Select(l => l.Source.Title).SequenceEqual(file.Expenses.Select(e => e.Title)));
    // Só a "Feira" é importada mesmo assim: uma adicionada (marcada como já existente), duas puladas.
    var one = ExpenseShare.Import(first, file, ExpenseShare.Suggest(first, file) with { ImportAnyway = new HashSet<Guid> { feira } });
    Equal(1, one.Result.Added.Count); Equal(2, one.Result.Duplicates); Equal(first.Expenses.Count + 1, one.Next.Expenses.Count);
    var line = one.Result.Lines.Single(l => l.Status == ShareLineStatus.Added); Equal(true, line.Existing && line.Expense!.Title == "Feira" && line.Expense.Id != feira);
    // Uma linha com erro aparece como erro, com a explicação.
    file.Expenses[0].Amount = 0; var broken = ExpenseShare.Import(receiver, file, ExpenseShare.Suggest(receiver, file)).Result.Lines[0]; Equal(true, broken.Status == ShareLineStatus.Error && broken.Error is not null);
});

// Duas pessoas do arquivo não podem virar a mesma pessoa aqui se dividem uma despesa: a divisão mudaria sem aviso.
Test("Two file people cannot map to the same person when they share an expense", () =>
{
    var sender = Sender(); var receiver = Receiver(); var before = Snapshot(receiver); var carla = receiver.People[0].Id;
    var both = new ShareMapping { People = new() { ["Ana"] = new(PersonAction.Existing, carla), ["Bruno"] = new(PersonAction.Existing, carla) } };
    var sharedBill = ExpenseShare.Build(sender, [sender.Expenses.First(e => e.Title == "Mercado").Id], false);
    try { ExpenseShare.Import(receiver, sharedBill, both); throw new Exception("Expected rejection"); } catch (BudgetException ex) { Equal(true, ex.Message.Contains("Ana") && ex.Message.Contains("Bruno")); }
    Equal(before, Snapshot(receiver));
    // Se eles não dividem nenhuma despesa do arquivo, ligar os dois à mesma pessoa é aceito.
    var separate = ExpenseShare.Build(sender, sender.Expenses.Where(e => e.Title is "Ração" or "Segredo").Select(e => e.Id).ToList(), false);
    Equal(2, ExpenseShare.Import(receiver, separate, both).Result.Added.Count);
});

// Tipos de arquivo não se misturam, e o arquivo de compartilhamento tem as mesmas proteções do backup.
Test("Share and backup files are different kinds and both are guarded", () =>
{
    var dir = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
    try
    {
        var sender = Sender(); var share = Path.Combine(dir, "despesas" + ShareFile.Extension); var backup = Path.Combine(dir, "backup.json");
        ExpenseShare.Save(share, ExpenseShare.Build(sender, [sender.Expenses[0].Id], false)); LocalStore.Export(backup, sender);
        Equal(1, ExpenseShare.Read(share).Expenses.Count); Equal(0, Directory.GetFiles(dir, "*.tmp").Length);
        // Backup como compartilhamento e compartilhamento como backup: recusados, com mensagem que diz o que o arquivo é.
        try { ExpenseShare.Read(backup); throw new Exception("Expected rejection"); } catch (BudgetException ex) { Equal(true, ex.Message.Contains("backup")); }
        try { LocalStore.Read(share); throw new Exception("Expected rejection"); } catch (BudgetException ex) { Equal(true, ex.Message.Contains("compartilhamento")); }
        // Um armazenamento ativo também recusa o compartilhamento como base, sem alterar o que tem.
        using (var store = new LocalStore(Path.Combine(dir, "base"))) { store.Save(sender); var saved = File.ReadAllText(store.FilePath); Reject(() => LocalStore.Read(share)); Equal(saved, File.ReadAllText(store.FilePath)); }
        var good = JsonNode.Parse(File.ReadAllText(share))!;
        void Tamper(Action<JsonNode> change) { var node = good.DeepClone(); change(node); File.WriteAllText(share, node.ToJsonString()); Reject(() => ExpenseShare.Read(share)); }
        Tamper(n => n["Kind"] = "backup"); Tamper(n => n["App"] = "Outro"); Tamper(n => n["FormatVersion"] = 2); Tamper(n => n["FormatVersion"] = 0); Tamper(n => n["Unknown"] = true);
        Tamper(n => n["Expenses"] = new JsonArray()); Tamper(n => n["Expenses"]![0]!["Participants"] = new JsonArray("Desconhecido")); Tamper(n => n["Expenses"]![0]!["OriginId"] = Guid.Empty.ToString());
        Tamper(n => n["Expenses"]![0]!["Extra"] = 1); Tamper(n => n["People"] = new JsonArray("Ana", "ana")); Tamper(n => n["Expenses"]![0]!["Category"] = "Inexistente");
        Tamper(n => n["Expenses"]!.AsArray().Add(n["Expenses"]![0]!.DeepClone())); // identificador de origem repetido
        Tamper(n => { var deep = new JsonObject(); var cursor = deep; for (var i = 0; i < 40; i++) { var inner = new JsonObject(); cursor["x"] = inner; cursor = inner; } n["People"] = deep; });
        foreach (var raw in new[] { "{", "null", "[]", "" }) { File.WriteAllText(share, raw); Reject(() => ExpenseShare.Read(share)); }
        using (var stream = File.OpenWrite(share)) stream.SetLength(6 * 1024 * 1024);
        Reject(() => ExpenseShare.Read(share));
        // Quantidade acima do limite também é recusada.
        var many = ExpenseShare.Build(sender, [sender.Expenses[0].Id], false); for (var i = 0; i < ExpenseShare.MaxExpenses; i++) many.Expenses.Add(many.Expenses[0] with { OriginId = Guid.NewGuid() });
        Reject(() => ExpenseShare.Validate(many));
    }
    finally { Directory.Delete(dir, true); }
});

// Tudo ou nada no disco: se a base resultante não é válida, nada é gravado e o arquivo atual e a cópia anterior ficam como estavam.
Test("Import is atomic: a failure leaves memory and disk untouched", () =>
{
    var dir = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid());
    try
    {
        var sender = Sender(); var receiver = Receiver();
        using var store = new LocalStore(dir); store.Save(receiver); var disk = File.ReadAllText(store.FilePath); var memory = Snapshot(receiver);
        var file = ExpenseShare.Build(sender, sender.Expenses.Where(e => e.Title != "Segredo").Select(e => e.Id).ToList(), true);
        // A última despesa do arquivo fica sem responsável (Bruno ignorado, sem escolha de quem assume): as anteriores já tinham sido
        // montadas na cópia de trabalho, mas a falha descarta tudo.
        var broken = new ShareMapping { People = new() { ["Ana"] = new(PersonAction.Create), ["Bruno"] = new(PersonAction.Ignore) } };
        Reject(() => { var (next, _) = ExpenseShare.Import(receiver, file, broken); store.Save(next); });
        Equal(memory, Snapshot(receiver)); Equal(disk, File.ReadAllText(store.FilePath)); Equal(false, File.Exists(store.BackupPath));
        // Com o mapeamento completo, salva de uma vez e a versão anterior vira a cópia automática.
        var (ok, result) = ExpenseShare.Import(receiver, file, ExpenseShare.Suggest(receiver, file)); store.Save(ok);
        Equal(6, result.Added.Count); Equal(disk, File.ReadAllText(store.BackupPath)); Equal(7, store.Load().Expenses.Count); Equal(2500m, store.Load().Incomes.Single().Amount);
    }
    finally { Directory.Delete(dir, true); }
});

// Executa toda a bateria, apresenta as falhas e produz um código de saída consumido pelo CI.
int failed = 0;
foreach (var test in tests)
{
    try { test.Test(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
}
Console.WriteLine($"\n{tests.Count - failed}/{tests.Count} tests passed.");
return failed == 0 ? 0 : 1;
