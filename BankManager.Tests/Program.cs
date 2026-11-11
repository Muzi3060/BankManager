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
        foreach (var json in new[] { "{", "null", "{\"Version\":3}", "{\"Version\":0}", "{\"UnknownField\":true}", "{\"People\":[null]}" }) { File.WriteAllText(path, json); Reject(() => LocalStore.Read(path)); }
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

// Um arquivo v1 é lido sem perda, ganha a versão 2 e, no primeiro salvamento, o v1 original vira o backup.
Test("Version 1 file migrates to 2 without losing data", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid()); Directory.CreateDirectory(path);
    try
    {
        var d = Family(); d.Expenses.Add(Bill(d, 800)); File.WriteAllText(Path.Combine(path, "financas.json"), V1Json(d));
        using var store = new LocalStore(path); var loaded = store.Load();
        Equal(2, loaded.Version); Equal(2, loaded.People.Count); Equal(800m, loaded.Expenses[0].Amount); Equal(0, loaded.Recurrences.Count);
        store.Save(loaded);
        Equal(1, (int)JsonNode.Parse(File.ReadAllText(store.BackupPath))!["Version"]!); Equal(2, (int)JsonNode.Parse(File.ReadAllText(store.FilePath))!["Version"]!);
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

// Executa toda a bateria, apresenta as falhas e produz um código de saída consumido pelo CI.
int failed = 0;
foreach (var test in tests)
{
    try { test.Test(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
}
Console.WriteLine($"\n{tests.Count - failed}/{tests.Count} tests passed.");
return failed == 0 ? 0 : 1;
