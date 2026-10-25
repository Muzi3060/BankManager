using System.Text.Json;
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
        foreach (var json in new[] { "{", "null", "{\"Version\":2}", "{\"UnknownField\":true}", "{\"People\":[null]}" }) { File.WriteAllText(path, json); Reject(() => LocalStore.Read(path)); }
        using (var stream = File.OpenWrite(path)) stream.SetLength(21 * 1024 * 1024);
        Reject(() => LocalStore.Read(path));
    }
    finally { File.Delete(path); }
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
