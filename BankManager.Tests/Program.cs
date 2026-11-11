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

// Executa toda a bateria, apresenta as falhas e produz um código de saída consumido pelo CI.
int failed = 0;
foreach (var test in tests)
{
    try { test.Test(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
}
Console.WriteLine($"\n{tests.Count - failed}/{tests.Count} tests passed.");
return failed == 0 ? 0 : 1;
