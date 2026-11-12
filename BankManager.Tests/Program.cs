using System.Text.Json;
using System.Text.Json.Nodes;
using BankManager.Nucleo;

// Executável de testes sem framework externo: registra cenários e retorna código não zero se algum falhar.
// As datas e os dados são sintéticos; os testes de disco usam apenas arquivos temporários.
var testes = new List<(string Nome, Action Teste)>();
var mes = new DateOnly(2026, 10, 1);
// Cria uma base independente para cada cenário, com duas pessoas e rendas na competência fixa do teste.
DadosDoOrcamento Familia(decimal a = 3000, decimal b = 1000)
{
    var dados = new DadosDoOrcamento { Pessoas = [new() { Nome = "Ana" }, new() { Nome = "Bruno" }] };
    dados.Rendas = [new() { IdDaPessoa = dados.Pessoas[0].Id, Mes = mes, Valor = a }, new() { IdDaPessoa = dados.Pessoas[1].Id, Mes = mes, Valor = b }]; return dados;
}
// Fábrica de despesa: modo individual seleciona só Ana; os demais modos incluem as duas pessoas.
Despesa Conta(DadosDoOrcamento d, decimal valor, ModoDeDivisao divisao = ModoDeDivisao.RendaDisponivel) => new() { Titulo = "Mercado", Categoria = "Mercado", Valor = valor, Vencimento = mes, Divisao = divisao, Participantes = divisao == ModoDeDivisao.Individual ? [d.Pessoas[0].Id] : d.Pessoas.Select(p => p.Id).ToList() };
// Compara resultados exatos, inclusive decimal, e lança uma exceção que o executor registra como falha.
void Igual<T>(T esperado, T obtido) { if (!EqualityComparer<T>.Default.Equals(esperado, obtido)) throw new Exception($"Expected {esperado}, got {obtido}"); }
// Exige falha de negócio: outra exceção indica um defeito e não deve ser aceita como validação correta.
void Rejeitar(Action acao) { try { acao(); } catch (ErroDeOrcamento) { return; } throw new Exception("Expected validation rejection"); }
// Registra o cenário para execução posterior, permitindo que uma falha não interrompa os outros testes.
void Teste(string nome, Action acao) => testes.Add((nome, acao));

// A divisão proporcional deve reproduzir o exemplo: disponíveis de 3.000/1.000 dividem 800 em 600/200.
Teste("75/25 proportional allocation", () => { var d = Familia(); var a = MotorFinanceiro.Ratear(d, Conta(d, 800)); Igual(600m, a.Valores[d.Pessoas[0].Id]); Igual(200m, a.Valores[d.Pessoas[1].Id]); });

// Uma conta individual de 2.000 reduz a disponibilidade de Ana para 1.000, igualando os pesos.
Teste("Individual debts reduce disposable income", () => { var d = Familia(); d.Despesas.Add(Conta(d, 2000, ModoDeDivisao.Individual)); var a = MotorFinanceiro.Ratear(d, Conta(d, 800)); Igual(400m, a.Valores[d.Pessoas[0].Id]); Igual(400m, a.Valores[d.Pessoas[1].Id]); });

// Quitar a conta individual não devolve esse dinheiro à capacidade mensal de pagar contas compartilhadas.
Teste("Paid individual debts still reduce disposable income", () => { var d = Familia(); var divida = Conta(d, 2000, ModoDeDivisao.Individual); divida.Paga = true; d.Despesas.Add(divida); Igual(400m, MotorFinanceiro.Ratear(d, Conta(d, 800)).Valores[d.Pessoas[0].Id]); });

// Sem renda para ninguém, a divisão deve ser igual e acompanhada de um aviso explícito.
Teste("Zero available income falls back explicitly", () => { var d = Familia(0, 0); var a = MotorFinanceiro.Ratear(d, Conta(d, 101)); Igual(50.5m, a.Valores[d.Pessoas[0].Id]); Igual(true, a.Aviso is not null); });

// Despesas individuais acima da renda zeram o peso, sem criar participação negativa.
Teste("Negative capacity contributes zero", () => { var d = Familia(100, 1000); d.Despesas.Add(Conta(d, 200, ModoDeDivisao.Individual)); var a = MotorFinanceiro.Ratear(d, Conta(d, 400)); Igual(0m, a.Valores[d.Pessoas[0].Id]); Igual(400m, a.Valores[d.Pessoas[1].Id]); });

// O modo individual atribui todo o lançamento ao único participante.
Teste("Individual expense entirely assigned", () => { var d = Familia(); Igual(123.45m, MotorFinanceiro.Ratear(d, Conta(d, 123.45m, ModoDeDivisao.Individual)).Valores[d.Pessoas[0].Id]); });

// O modo igual não usa a diferença de salário como peso.
Teste("Equal split ignores salary", () => { var d = Familia(); Igual(50m, MotorFinanceiro.Ratear(d, Conta(d, 100, ModoDeDivisao.Igual)).Valores[d.Pessoas[0].Id]); });

// Verifica conservação do total, precisão de centavos e estabilidade do rateio em 1.000 amostras.
Teste("Cent rounding preserves totals across 1000 samples", () =>
{
    // Semente fixa torna os valores reproduzíveis; os Guids variam, mas a repetição usa a mesma base de pessoas.
    var aleatorio = new Random(42);
    for (int n = 0; n < 1000; n++)
    {
        var d = Familia(aleatorio.Next(0, 1000000), aleatorio.Next(0, 1000000)); var valorLido = aleatorio.Next(1, 10000000) / 100m;
        var a = MotorFinanceiro.Ratear(d, Conta(d, valorLido)); Igual(valorLido, a.Valores.Values.Sum());
        Igual(true, a.Valores.Values.All(v => v >= 0 && decimal.Round(v, 2) == v));
        var deNovo = MotorFinanceiro.Ratear(d, Conta(d, valorLido)); Igual(a.Valores[d.Pessoas[0].Id], deNovo.Valores[d.Pessoas[0].Id]);
    }
});

// Quem tem peso zero não pode receber nem o centavo residual do arredondamento.
Teste("Zero weight never receives rounding remainder", () => { var d = Familia(0, 100); var a = MotorFinanceiro.Ratear(d, Conta(d, .01m)); Igual(0m, a.Valores[d.Pessoas[0].Id]); Igual(.01m, a.Valores[d.Pessoas[1].Id]); });

// Rendas novas não alteram a competência anterior e não são propagadas para outra pessoa.
Teste("Monthly incomes are isolated", () => { var d = Familia(); d.Rendas.Add(new() { IdDaPessoa = d.Pessoas[0].Id, Mes = mes.AddMonths(1), Valor = 5000 }); Igual(3000m, MotorFinanceiro.Renda(d, d.Pessoas[0].Id, mes)); Igual(0m, MotorFinanceiro.Renda(d, d.Pessoas[1].Id, mes.AddMonths(1))); });

// O saldo desconta a responsabilidade uma vez, separando os valores já quitados.
Teste("Dashboard paid and planned balances", () => { var d = Familia(); var e = Conta(d, 800); e.Paga = true; d.Despesas.Add(e); var a = MotorFinanceiro.Saldos(d, mes)[0]; Igual(600m, a.Pago); Igual(2400m, a.Sobra); });

// Parcelamento deve preservar total, identificadores únicos e o dia de referência após fevereiro.
Teste("Installments preserve total and month-end anchor", () => { var d = Familia(); var e = Conta(d, 100); e.Vencimento = new(2028, 1, 31); var partes = RegrasDoOrcamento.Parcelar(e, 3); Igual(100m, partes.Sum(p => p.Valor)); Igual(33.34m, partes[0].Valor); Igual(new DateOnly(2028, 2, 29), partes[1].Vencimento); Igual(new DateOnly(2028, 3, 31), partes[2].Vencimento); Igual(3, partes.Select(p => p.Id).Distinct().Count()); });

// Parcelas de menos de um centavo e quantidades acima do limite precisam ser recusadas.
Teste("Too-small and too-many installments rejected", () => { var d = Familia(); Rejeitar(() => RegrasDoOrcamento.Parcelar(Conta(d, .01m), 2)); Rejeitar(() => RegrasDoOrcamento.Parcelar(Conta(d, 100), 121)); });

// Com apenas um mês de mercado, a estimativa do mês seguinte é o mesmo valor.
Teste("One month market forecast equals actual", () => { var d = Familia(); d.Despesas.Add(Conta(d, 1000)); Igual(1000m, MotorFinanceiro.Previsoes(d, mes.AddMonths(1)).Single().Estimativa); });

// Meses sem registros dentro da janela observada contam como zero na média.
Teste("Forecast includes zero months in observed window", () => { var d = Familia(); d.Despesas.Add(Conta(d, 900)); var estimativa = MotorFinanceiro.Previsoes(d, mes.AddMonths(3)).Single(); Igual(300m, estimativa.Historico); Igual(3, estimativa.Meses); });

// Gastos anteriores aos três meses da janela não podem continuar na previsão.
Teste("Forecast excludes older history", () => { var d = Familia(); d.Despesas.Add(Conta(d, 900)); Igual(0, MotorFinanceiro.Previsoes(d, mes.AddMonths(4)).Count); });

// Um compromisso à vista já previsto pela média não deve ser somado novamente.
Teste("Scheduled ordinary expenses do not double count history", () => { var d = Familia(); d.Despesas.Add(Conta(d, 1000)); var proximo = Conta(d, 600); proximo.Vencimento = mes.AddMonths(1); d.Despesas.Add(proximo); Igual(1000m, MotorFinanceiro.Previsoes(d, mes.AddMonths(1)).Single().Estimativa); proximo.Valor = 1300; Igual(1300m, MotorFinanceiro.Previsoes(d, mes.AddMonths(1)).Single().Estimativa); });

// Parcelas só são previstas nos seus vencimentos e desaparecem ao terminar a dívida.
Teste("Installments counted only on due month", () => { var d = Familia(); d.Despesas.AddRange(RegrasDoOrcamento.Parcelar(Conta(d, 900), 3)); Igual(300m, MotorFinanceiro.Previsoes(d, mes.AddMonths(1)).Single().Estimativa); Igual(0, MotorFinanceiro.Previsoes(d, mes.AddMonths(3)).Count); });

// Registros posteriores ao alvo não podem influenciar uma previsão de meses anteriores.
Teste("Future records cannot leak into historical forecast", () => { var d = Familia(); var e = Conta(d, 1000); e.Vencimento = mes.AddMonths(5); d.Despesas.Add(e); Igual(0, MotorFinanceiro.Previsoes(d, mes.AddMonths(1)).Count); });

// A entrada brasileira aceita vírgula decimal e zero quando permitido.
Teste("Brazilian money parses exactly", () => { Igual(1000.50m, Dinheiro.Interpretar("1000,50", "Valor")); Igual(0m, Dinheiro.Interpretar("0", "Valor")); });

// Rejeita formatos ambíguos, negativos, notação científica, símbolos e precisão além do limite.
Teste("Ambiguous negative exponent and excessive precision rejected", () => { foreach (var entrada in new[] { "1.000", "1,000", "-1", "1e3", "NaN", "", "R$ 10", "1000000000", "1,234" }) Rejeitar(() => Dinheiro.Interpretar(entrada, "Valor")); Rejeitar(() => Dinheiro.Interpretar("0", "Valor", true)); });

// Nomes repetidos e responsáveis inexistentes devem impedir o salvamento.
Teste("Duplicate names and invalid identity rejected", () => { var d = Familia(); d.Pessoas[1].Nome = " ana "; Rejeitar(() => RegrasDoOrcamento.Validar(d)); d.Pessoas[1].Nome = "Bruno"; d.Despesas.Add(Conta(d, 10) with { Participantes = [Guid.NewGuid()] }); Rejeitar(() => RegrasDoOrcamento.Validar(d)); });

// O modo individual exige um responsável; valores desconhecidos do enum também são inválidos.
Teste("Participant count and enum validated", () => { var d = Familia(); d.Despesas.Add(Conta(d, 10) with { Divisao = ModoDeDivisao.Individual }); Rejeitar(() => RegrasDoOrcamento.Validar(d)); d.Despesas[0].Divisao = (ModoDeDivisao)90; Rejeitar(() => RegrasDoOrcamento.Validar(d)); });

// Cada pessoa só pode ter uma renda por mês e as competências devem estar no intervalo suportado.
Teste("Dates and income uniqueness validated", () => { var d = Familia(); d.Rendas.Add(d.Rendas[0] with { }); Rejeitar(() => RegrasDoOrcamento.Validar(d)); d.Rendas.RemoveAt(2); d.Rendas[0].Mes = new(1999, 1, 1); Rejeitar(() => RegrasDoOrcamento.Validar(d)); });

// O total de itens precisa coincidir com a despesa; um backup adulterado não pode impor outro total.
Teste("Item arithmetic and tampered total validated", () => { var d = Familia(); var e = Conta(d, 5); e.Itens.Add(new() { Nome = "Arroz", Quantidade = 2, PrecoUnitario = 2.50m }); d.Despesas.Add(e); RegrasDoOrcamento.Validar(d); e.Valor = 6; Rejeitar(() => RegrasDoOrcamento.Validar(d)); });

// Quantidade fracionária que resulta em meio centavo deve arredondar para longe de zero.
Teste("Fractional items round away from zero", () => { Igual(.01m, new ItemDaDespesa { Nome = "Peso", Quantidade = .5m, PrecoUnitario = .01m }.Total); });

// Coleções nulas vindas de JSON devem causar erro de negócio, não acesso a referência nula.
Teste("Null collections rejected", () => { var d = Familia(); d.Despesas = null!; Rejeitar(() => RegrasDoOrcamento.Validar(d)); });

// Nomes não podem conter caracteres de controle que deformem a apresentação.
Teste("Text control characters rejected", () => { var d = Familia(); d.Pessoas[0].Nome = "Ana\nBruno"; Rejeitar(() => RegrasDoOrcamento.Validar(d)); });

// Confere leitura após gravação, cópia anterior, preservação após falha e permissões privadas no Unix.
Teste("Persistence roundtrip backup and invalid-save preservation", () =>
{
    var caminho = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid());
    try
    {
        using var armazenamento = new ArmazenamentoLocal(caminho); var d = Familia(); d.Despesas.Add(Conta(d, 800)); armazenamento.Salvar(d);
        Igual(800m, armazenamento.Carregar().Despesas[0].Valor); d.Despesas[0].Valor = 900; armazenamento.Salvar(d);
        Igual(800m, ArmazenamentoLocal.Ler(armazenamento.CaminhoDaCopia).Despesas[0].Valor); d.Despesas[0].Valor = -1;
        Rejeitar(() => armazenamento.Salvar(d)); Igual(900m, armazenamento.Carregar().Despesas[0].Valor);
        if (!OperatingSystem.IsWindows()) Igual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(armazenamento.CaminhoDoArquivo));
    }
    finally { Directory.Delete(caminho, true); }
});

// Uma segunda instância cooperante não pode adquirir o mesmo arquivo de bloqueio.
Teste("Second writer is prevented", () =>
{
    var caminho = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid());
    try { using var armazenamento = new ArmazenamentoLocal(caminho); Rejeitar(() => { using var duplicada = new ArmazenamentoLocal(caminho); }); }
    finally { Directory.Delete(caminho, true); }
});

// Recusa JSON malformado, versão desconhecida, campos estranhos, registros nulos e arquivo muito grande.
Teste("Malformed and unsupported JSON cannot import", () =>
{
    var caminho = Path.GetTempFileName();
    try
    {
        foreach (var json in new[] { "{", "null", "{\"Version\":4}", "{\"Version\":0}", "{\"UnknownField\":true}", "{\"People\":[null]}" }) { File.WriteAllText(caminho, json); Rejeitar(() => ArmazenamentoLocal.Ler(caminho)); }
        using (var fluxo = File.OpenWrite(caminho)) fluxo.SetLength(21 * 1024 * 1024);
        Rejeitar(() => ArmazenamentoLocal.Ler(caminho));
    }
    finally { File.Delete(caminho); }
});
// A situação de vencimento: pago prevalece; atraso começa no dia seguinte; "em breve" vale de hoje a 3 dias.
Teste("Due status classification", () =>
{
    var d = Familia(); var hoje = new DateOnly(2026, 11, 11);
    SituacaoDoPrazo Em(int deslocamento, bool paga = false) { var e = Conta(d, 10); e.Vencimento = hoje.AddDays(deslocamento); e.Paga = paga; return SituacaoDoPrazo.De(e, hoje); }
    Igual(TipoDePrazo.Paga, Em(-10, true).Tipo); Igual(TipoDePrazo.Atrasada, Em(-1).Tipo); Igual(-1, Em(-1).Dias);
    Igual(TipoDePrazo.VenceEmBreve, Em(0).Tipo); Igual(TipoDePrazo.VenceEmBreve, Em(3).Tipo); Igual(TipoDePrazo.EmAberto, Em(4).Tipo); Igual(1, Em(1).Dias);
});

// A preferência de tema é gravada em settings.json e lida de volta; o padrão é seguir o sistema.
Teste("Theme preference roundtrip and default", () =>
{
    var caminho = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid());
    try
    {
        Igual(PreferenciaDeTema.Automatico, ArmazenamentoDePreferencias.Carregar(caminho).Tema);
        ArmazenamentoDePreferencias.Salvar(caminho, new Preferencias { Tema = PreferenciaDeTema.Escuro });
        Igual(PreferenciaDeTema.Escuro, ArmazenamentoDePreferencias.Carregar(caminho).Tema);
        Igual(true, File.ReadAllText(Path.Combine(caminho, ArmazenamentoDePreferencias.NomeDoArquivo)).Contains("\"Dark\""));
    }
    finally { Directory.Delete(caminho, true); }
});

// Preferência corrompida ou com valor desconhecido nunca impede abrir o app: volta ao padrão.
Teste("Corrupt settings fall back to defaults", () =>
{
    var caminho = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid()); Directory.CreateDirectory(caminho);
    try
    {
        foreach (var json in new[] { "{", "null", "{\"Theme\":\"Roxo\"}", "{\"Theme\":99}", "" })
        {
            File.WriteAllText(Path.Combine(caminho, ArmazenamentoDePreferencias.NomeDoArquivo), json);
            Igual(PreferenciaDeTema.Automatico, ArmazenamentoDePreferencias.Carregar(caminho).Tema);
        }
    }
    finally { Directory.Delete(caminho, true); }
});

// Trocar o tema não pode tocar nos dados financeiros: o financas.json fica byte a byte igual.
Teste("Saving settings leaves financial data untouched", () =>
{
    var caminho = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid());
    try
    {
        using var armazenamento = new ArmazenamentoLocal(caminho); var d = Familia(); d.Despesas.Add(Conta(d, 800)); armazenamento.Salvar(d);
        var antes = File.ReadAllBytes(armazenamento.CaminhoDoArquivo);
        ArmazenamentoDePreferencias.Salvar(caminho, new Preferencias { Tema = PreferenciaDeTema.Claro });
        Igual(true, antes.SequenceEqual(File.ReadAllBytes(armazenamento.CaminhoDoArquivo)));
        Igual(false, File.ReadAllText(armazenamento.CaminhoDoArquivo).Contains("Theme"));
    }
    finally { Directory.Delete(caminho, true); }
});

// ---- Esquema v2: despesas recorrentes ----
var hoje = new DateOnly(2026, 11, 11);
// Modelo de aluguel que vence no dia 31, útil para testar meses curtos; começa em janeiro de 2026.
Recorrencia Aluguel(DadosDoOrcamento d, int dia = 31, DateOnly? inicio = null) => new() { Titulo = "Aluguel", Categoria = "Moradia", Valor = 1000, DiaDoMes = dia, Divisao = ModoDeDivisao.Igual, Participantes = d.Pessoas.Select(p => p.Id).ToList(), MesInicial = inicio ?? new DateOnly(2026, 1, 1) };
// Reproduz um arquivo real da versão 1: sem a lista de recorrências e sem os campos novos das despesas.
string JsonV1(DadosDoOrcamento d)
{
    var no = JsonNode.Parse(JsonSerializer.Serialize(d, ArmazenamentoLocal.OpcoesJson))!; no["Version"] = 1; no.AsObject().Remove("Recurrences");
    foreach (var e in no["Expenses"]!.AsArray()) { e!.AsObject().Remove("RecurrenceId"); e.AsObject().Remove("RecurrenceMonth"); }
    return no.ToJsonString();
}

// Um arquivo v1 é lido sem perda, ganha a versão atual e, no primeiro salvamento, o v1 original vira o backup.
Teste("Version 1 file migrates to the current version without losing data", () =>
{
    var caminho = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid()); Directory.CreateDirectory(caminho);
    try
    {
        var d = Familia(); d.Despesas.Add(Conta(d, 800)); File.WriteAllText(Path.Combine(caminho, "financas.json"), JsonV1(d));
        using var armazenamento = new ArmazenamentoLocal(caminho); var carregado = armazenamento.Carregar();
        Igual(DadosDoOrcamento.VersaoAtual, carregado.Versao); Igual(2, carregado.Pessoas.Count); Igual(800m, carregado.Despesas[0].Valor); Igual(0, carregado.Recorrencias.Count);
        armazenamento.Salvar(carregado);
        Igual(1, (int)JsonNode.Parse(File.ReadAllText(armazenamento.CaminhoDaCopia))!["Version"]!); Igual(DadosDoOrcamento.VersaoAtual, (int)JsonNode.Parse(File.ReadAllText(armazenamento.CaminhoDoArquivo))!["Version"]!);
        Igual(800m, armazenamento.Carregar().Despesas[0].Valor);
    }
    finally { Directory.Delete(caminho, true); }
});

// Um arquivo que diz ser v1 mas traz recorrências está adulterado: é recusado em vez de adivinhado.
Teste("Version 1 file with recurrences is rejected", () =>
{
    var d = Familia(); d.Recorrencias.Add(Aluguel(d)); var caminho = Path.GetTempFileName();
    try { var no = JsonNode.Parse(JsonSerializer.Serialize(d, ArmazenamentoLocal.OpcoesJson))!; no["Version"] = 1; File.WriteAllText(caminho, no.ToJsonString()); Rejeitar(() => ArmazenamentoLocal.Ler(caminho)); }
    finally { File.Delete(caminho); }
});

// Do mês inicial ao horizonte (12 meses à frente), dia 31 vira o último dia dos meses curtos, e repetir não duplica nada.
Teste("Recurrence generates months up to the horizon and is idempotent", () =>
{
    var d = Familia(); d.Recorrencias.Add(Aluguel(d));
    Igual(23, MotorDeRecorrencias.Gerar(d, hoje)); // jan/2026 a nov/2027
    Igual(0, MotorDeRecorrencias.Gerar(d, hoje));
    var vencimento = d.Despesas.ToDictionary(e => e.MesDaRecorrencia!.Value, e => e.Vencimento);
    Igual(new DateOnly(2026, 2, 28), vencimento[new DateOnly(2026, 2, 1)]); Igual(new DateOnly(2026, 3, 31), vencimento[new DateOnly(2026, 3, 1)]); Igual(new DateOnly(2027, 11, 30), vencimento[new DateOnly(2027, 11, 1)]);
    Igual(false, d.Despesas.Any(e => e.Paga)); RegrasDoOrcamento.Validar(d);
    // O rateio das ocorrências usa as regras de sempre: a soma fecha com o valor da despesa.
    Igual(1000m, MotorFinanceiro.Ratear(d, d.Despesas[0]).Valores.Values.Sum());
});

// Com o passar do tempo, a janela avança e só os meses novos são criados.
Teste("Recurrence tops up as time passes", () =>
{
    var d = Familia(); d.Recorrencias.Add(Aluguel(d)); MotorDeRecorrencias.Gerar(d, hoje);
    Igual(2, MotorDeRecorrencias.Gerar(d, new DateOnly(2027, 1, 5))); // dez/2027 e jan/2028
    Igual(25, d.Despesas.Count);
});

// O horizonte respeita o limite de 2099 e um início distante ainda cria o primeiro mês.
Teste("Horizon clamp and far-future start", () =>
{
    Igual(new DateOnly(2099, 12, 1), MotorDeRecorrencias.Horizonte(new DateOnly(2099, 6, 15)));
    var d = Familia(); d.Recorrencias.Add(Aluguel(d, 5, new DateOnly(2030, 1, 1)));
    Igual(1, MotorDeRecorrencias.Gerar(d, hoje)); Igual(new DateOnly(2030, 1, 5), d.Despesas[0].Vencimento);
});

// Excluir um mês de uma recorrência registra o mês como ignorado: a geração não o recria.
Teste("Deleted occurrence is not regenerated", () =>
{
    var d = Familia(); d.Recorrencias.Add(Aluguel(d)); MotorDeRecorrencias.Gerar(d, hoje);
    var marco = d.Despesas.First(e => e.MesDaRecorrencia == new DateOnly(2026, 3, 1)); MotorDeRecorrencias.RemoverDespesa(d, marco.Id);
    Igual(0, MotorDeRecorrencias.Gerar(d, hoje)); Igual(22, d.Despesas.Count); Igual(1, d.Recorrencias[0].MesesPulados.Count); RegrasDoOrcamento.Validar(d);
});

// Encerrar remove só o que está em aberto depois do mês final; o que já foi pago permanece no histórico.
Teste("Ending a recurrence keeps paid history", () =>
{
    var d = Familia(); d.Recorrencias.Add(Aluguel(d)); MotorDeRecorrencias.Gerar(d, hoje);
    var dezembro = d.Despesas.First(e => e.MesDaRecorrencia == new DateOnly(2026, 12, 1)); dezembro.Paga = true;
    MotorDeRecorrencias.Encerrar(d, d.Recorrencias[0].Id, new DateOnly(2026, 11, 20));
    Igual(new DateOnly(2026, 11, 1), d.Recorrencias[0].MesFinal!.Value); Igual(11 + 1, d.Despesas.Count); // jan..nov + dez pago
    Igual(0, MotorDeRecorrencias.Gerar(d, hoje)); RegrasDoOrcamento.Validar(d);
    Rejeitar(() => MotorDeRecorrencias.Encerrar(d, d.Recorrencias[0].Id, new DateOnly(2026, 12, 1)));
});

// Não se encerra antes do início; remover o modelo mantém os lançamentos como despesas comuns.
Teste("End before start is rejected and removing the model keeps expenses", () =>
{
    var d = Familia(); d.Recorrencias.Add(Aluguel(d)); MotorDeRecorrencias.Gerar(d, hoje);
    Rejeitar(() => MotorDeRecorrencias.Encerrar(d, d.Recorrencias[0].Id, new DateOnly(2025, 12, 1)));
    var quantidade = d.Despesas.Count; MotorDeRecorrencias.Remover(d, d.Recorrencias[0].Id);
    Igual(quantidade, d.Despesas.Count); Igual(true, d.Despesas.All(e => e.IdDaRecorrencia is null && e.MesDaRecorrencia is null)); RegrasDoOrcamento.Validar(d);
});

// Dados de recorrência inconsistentes nunca são salvos nem importados.
Teste("Invalid recurrence data is rejected", () =>
{
    DadosDoOrcamento Novo() { var d = Familia(); d.Recorrencias.Add(Aluguel(d)); MotorDeRecorrencias.Gerar(d, hoje); return d; }
    var d = Novo(); d.Recorrencias[0].DiaDoMes = 0; Rejeitar(() => RegrasDoOrcamento.Validar(d));
    d = Novo(); d.Recorrencias[0].DiaDoMes = 32; Rejeitar(() => RegrasDoOrcamento.Validar(d));
    d = Novo(); d.Recorrencias[0].MesFinal = new DateOnly(2025, 12, 1); Rejeitar(() => RegrasDoOrcamento.Validar(d));
    d = Novo(); d.Recorrencias[0].MesInicial = new DateOnly(2026, 1, 15); Rejeitar(() => RegrasDoOrcamento.Validar(d));
    d = Novo(); d.Recorrencias[0].Participantes = []; Rejeitar(() => RegrasDoOrcamento.Validar(d));
    d = Novo(); d.Recorrencias[0].Valor = 0; Rejeitar(() => RegrasDoOrcamento.Validar(d));
    d = Novo(); d.Despesas[0].IdDaRecorrencia = Guid.NewGuid(); Rejeitar(() => RegrasDoOrcamento.Validar(d));          // modelo inexistente
    d = Novo(); d.Despesas[0].MesDaRecorrencia = null; Rejeitar(() => RegrasDoOrcamento.Validar(d));                  // vínculo pela metade
    d = Novo(); d.Despesas[1].MesDaRecorrencia = d.Despesas[0].MesDaRecorrencia; Rejeitar(() => RegrasDoOrcamento.Validar(d)); // mês repetido
    d = Novo(); d.Despesas[0].TotalDeParcelas = 2; d.Despesas[0].GrupoDeParcelas = Guid.NewGuid(); Rejeitar(() => RegrasDoOrcamento.Validar(d)); // recorrente não parcela
});

// A recorrência sobrevive a gravar e ler (inclusive os meses ignorados).
Teste("Recurrence persistence roundtrip", () =>
{
    var caminho = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid());
    try
    {
        using var armazenamento = new ArmazenamentoLocal(caminho); var d = Familia(); d.Recorrencias.Add(Aluguel(d)); MotorDeRecorrencias.Gerar(d, hoje);
        MotorDeRecorrencias.RemoverDespesa(d, d.Despesas[3].Id); armazenamento.Salvar(d); var retorno = armazenamento.Carregar();
        Igual(1, retorno.Recorrencias.Count); Igual(22, retorno.Despesas.Count); Igual(1, retorno.Recorrencias[0].MesesPulados.Count); Igual(0, MotorDeRecorrencias.Gerar(retorno, hoje));
    }
    finally { Directory.Delete(caminho, true); }
});

// ---- Quem pagou e acerto de contas ----
// Marca como paga por alguém; PagaPor nulo é "não informado".
Despesa PagaPelaPessoa(DadosDoOrcamento d, decimal valor, Guid? pagador, ModoDeDivisao divisao = ModoDeDivisao.RendaDisponivel) { var e = Conta(d, valor, divisao); e.Paga = true; e.PagaPor = pagador; d.Despesas.Add(e); return e; }
decimal Deve(List<Divida> dividas, Guid devedor, Guid credor) => dividas.Where(x => x.IdDoDevedor == devedor && x.IdDoCredor == credor).Sum(x => x.Valor);

// Ana paga 800 e a divisão proporcional dá 600/200: Bruno deve 200 a Ana, e Ana não deve nada a si mesma.
Teste("Payer is owed the others' shares", () =>
{
    var d = Familia(); var (ana, bruno) = (d.Pessoas[0].Id, d.Pessoas[1].Id); PagaPelaPessoa(d, 800, ana);
    var dividas = MotorDeAcertos.Dividas(d); Igual(1, dividas.Count); Igual(200m, Deve(dividas, bruno, ana));
});

// Despesas em aberto e pagas sem pagador informado não geram dívida (comportamento antigo preservado).
Teste("Unpaid and unattributed payments create no debt", () =>
{
    var d = Familia(); d.Despesas.Add(Conta(d, 800)); PagaPelaPessoa(d, 500, null);
    Igual(0, MotorDeAcertos.Dividas(d).Count);
});

// Quem paga uma despesa individual de outra pessoa passa a ter o valor inteiro a receber; a própria despesa individual não gera dívida.
Teste("Paying someone else's individual expense creates a full debt", () =>
{
    var d = Familia(); var (ana, bruno) = (d.Pessoas[0].Id, d.Pessoas[1].Id);
    var e = PagaPelaPessoa(d, 100, ana); e.Divisao = ModoDeDivisao.Individual; e.Participantes = [bruno]; // Bruno é o responsável, Ana pagou
    Igual(100m, Deve(MotorDeAcertos.Dividas(d), bruno, ana));
    var proprias = PagaPelaPessoa(d, 50, ana, ModoDeDivisao.Individual); // Ana paga a sua própria
    Igual(100m, Deve(MotorDeAcertos.Dividas(d), bruno, ana)); Igual(1, MotorDeAcertos.Dividas(d).Count);
});

// As dívidas de um par se compensam: 200 a favor de Ana menos 100 a favor de Bruno deixa Bruno devendo 100.
Teste("Mutual debts net out within a pair", () =>
{
    var d = Familia(); var (ana, bruno) = (d.Pessoas[0].Id, d.Pessoas[1].Id);
    PagaPelaPessoa(d, 800, ana);                           // Bruno deve 200 a Ana
    PagaPelaPessoa(d, 600, bruno, ModoDeDivisao.Igual);        // Ana deve 300 a Bruno
    var dividas = MotorDeAcertos.Dividas(d); Igual(1, dividas.Count); Igual(100m, Deve(dividas, ana, bruno));
});

// Um acerto abate a dívida; pagar a mais inverte o sentido; pagar exatamente quita.
Teste("Settlements reduce, clear and flip the balance", () =>
{
    var d = Familia(); var (ana, bruno) = (d.Pessoas[0].Id, d.Pessoas[1].Id); PagaPelaPessoa(d, 800, ana);
    d.Acertos.Add(new Acerto { IdDoPagador = bruno, IdDoRecebedor = ana, Valor = 150 }); Igual(50m, Deve(MotorDeAcertos.Dividas(d), bruno, ana));
    d.Acertos.Add(new Acerto { IdDoPagador = bruno, IdDoRecebedor = ana, Valor = 50 }); Igual(0, MotorDeAcertos.Dividas(d).Count);
    d.Acertos.Add(new Acerto { IdDoPagador = bruno, IdDoRecebedor = ana, Valor = 30 }); Igual(30m, Deve(MotorDeAcertos.Dividas(d), ana, bruno)); RegrasDoOrcamento.Validar(d);
});

// Com três pessoas, cada par é calculado separadamente: ninguém ganha dívida com quem não pagou nada por ele.
Teste("Three people settle pairwise", () =>
{
    var d = Familia(); d.Pessoas.Add(new Pessoa { Nome = "Carla" }); var (ana, bruno, carla) = (d.Pessoas[0].Id, d.Pessoas[1].Id, d.Pessoas[2].Id);
    var e = Conta(d, 900, ModoDeDivisao.Igual); e.Participantes = [ana, bruno, carla]; e.Paga = true; e.PagaPor = ana; d.Despesas.Add(e);
    var dividas = MotorDeAcertos.Dividas(d); Igual(2, dividas.Count); Igual(300m, Deve(dividas, bruno, ana)); Igual(300m, Deve(dividas, carla, ana));
});

// Centavos não se perdem: 100,00 em três partes (33,34/33,33/33,33) soma exatamente o que o pagador adiantou pelos outros.
Teste("Debts add up to the exact amount advanced for others", () =>
{
    var d = Familia(); d.Pessoas.Add(new Pessoa { Nome = "Carla" }); var ids = d.Pessoas.Select(p => p.Id).ToList();
    var e = Conta(d, 100, ModoDeDivisao.Igual); e.Participantes = ids; e.Paga = true; e.PagaPor = ids[0]; d.Despesas.Add(e);
    var compartilhar = MotorFinanceiro.Ratear(d, e).Valores[ids[0]];
    Igual(100m - compartilhar, MotorDeAcertos.Dividas(d).Sum(x => x.Valor));
});

// Quem pagou só vale em despesa paga e com pessoa existente; acertos precisam de duas pessoas diferentes e valor positivo.
Teste("Invalid payer and settlement data is rejected", () =>
{
    var d = Familia(); var (ana, bruno) = (d.Pessoas[0].Id, d.Pessoas[1].Id);
    var emAberto = Conta(d, 100); emAberto.PagaPor = ana; d.Despesas.Add(emAberto); Rejeitar(() => RegrasDoOrcamento.Validar(d));
    d = Familia(); PagaPelaPessoa(d, 100, Guid.NewGuid()); Rejeitar(() => RegrasDoOrcamento.Validar(d));
    d = Familia(); d.Acertos.Add(new Acerto { IdDoPagador = ana, IdDoRecebedor = ana, Valor = 10 }); Rejeitar(() => RegrasDoOrcamento.Validar(d));
    d = Familia(); d.Acertos.Add(new Acerto { IdDoPagador = ana, IdDoRecebedor = bruno, Valor = 0 }); Rejeitar(() => RegrasDoOrcamento.Validar(d));
    d = Familia(); d.Acertos.Add(new Acerto { IdDoPagador = ana, IdDoRecebedor = Guid.NewGuid(), Valor = 10 }); Rejeitar(() => RegrasDoOrcamento.Validar(d));
    d = Familia(); d.Acertos.Add(new Acerto { IdDoPagador = ana, IdDoRecebedor = bruno, Valor = 10.005m }); Rejeitar(() => RegrasDoOrcamento.Validar(d));
});

// Quem pagou e os acertos sobrevivem a gravar e ler; um arquivo v2 antigo (sem esses campos) continua abrindo.
Teste("Payer and settlements persist; older v2 files still open", () =>
{
    var caminho = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid());
    try
    {
        using var armazenamento = new ArmazenamentoLocal(caminho); var d = Familia(); var (ana, bruno) = (d.Pessoas[0].Id, d.Pessoas[1].Id); PagaPelaPessoa(d, 800, ana);
        d.Acertos.Add(new Acerto { IdDoPagador = bruno, IdDoRecebedor = ana, Valor = 150 }); armazenamento.Salvar(d);
        var retorno = armazenamento.Carregar(); Igual(ana, retorno.Despesas[0].PagaPor!.Value); Igual(1, retorno.Acertos.Count); Igual(50m, Deve(MotorDeAcertos.Dividas(retorno), bruno, ana));
        // Fabrica um arquivo "v2 anterior": sem Settlements e sem PagaPor.
        var no = JsonNode.Parse(JsonSerializer.Serialize(d, ArmazenamentoLocal.OpcoesJson))!; no.AsObject().Remove("Settlements"); foreach (var e in no["Expenses"]!.AsArray()) e!.AsObject().Remove("PaidBy");
        File.WriteAllText(armazenamento.CaminhoDoArquivo, no.ToJsonString()); var antigo = armazenamento.Carregar(); Igual(0, antigo.Acertos.Count); Igual(true, antigo.Despesas[0].PagaPor is null); Igual(0, MotorDeAcertos.Dividas(antigo).Count);
    }
    finally { Directory.Delete(caminho, true); }
});

// Um arquivo v1 que traga quem pagou ou acertos é adulterado: recusado.
Teste("Version 1 file with payer data is rejected", () =>
{
    var d = Familia(); PagaPelaPessoa(d, 100, d.Pessoas[0].Id); var caminho = Path.GetTempFileName();
    try { var no = JsonNode.Parse(JsonSerializer.Serialize(d, ArmazenamentoLocal.OpcoesJson))!; no["Version"] = 1; no.AsObject().Remove("Recurrences"); File.WriteAllText(caminho, no.ToJsonString()); Rejeitar(() => ArmazenamentoLocal.Ler(caminho)); }
    finally { File.Delete(caminho); }
});

// ---- Várias rendas por pessoa ----
// A renda do mês é a soma das linhas (salário + extra) e o rateio proporcional usa esse total.
Teste("Income lines add up and drive the split", () =>
{
    var d = Familia(0, 1000); d.Rendas.Add(new RendaMensal { IdDaPessoa = d.Pessoas[0].Id, Mes = mes, Valor = 1000, Rotulo = "Salário" }); d.Rendas.Add(new RendaMensal { IdDaPessoa = d.Pessoas[0].Id, Mes = mes, Valor = 2000, Rotulo = "Extra" });
    Igual(3000m, MotorFinanceiro.Renda(d, d.Pessoas[0].Id, mes)); Igual(3, MotorFinanceiro.LinhasDeRenda(d, d.Pessoas[0].Id, mes).Count); // a linha "Renda" de zero criada por Family + Salário + Extra
    Igual(600m, MotorFinanceiro.Ratear(d, Conta(d, 800)).Valores[d.Pessoas[0].Id]); RegrasDoOrcamento.Validar(d);
});

// Nomes repetidos, mais de 20 linhas, nome vazio e identificador repetido são recusados.
Teste("Income line validation", () =>
{
    var d = Familia(); d.Rendas.Add(new RendaMensal { IdDaPessoa = d.Pessoas[0].Id, Mes = mes, Valor = 10, Rotulo = " salário" }); d.Rendas[0].Rotulo = "Salário"; Rejeitar(() => RegrasDoOrcamento.Validar(d));
    d = Familia(); d.Rendas[0].Rotulo = ""; Rejeitar(() => RegrasDoOrcamento.Validar(d));
    d = Familia(); d.Rendas.Add(d.Rendas[0] with { Rotulo = "Outra" }); Rejeitar(() => RegrasDoOrcamento.Validar(d)); // mesmo Id
    d = Familia(); for (var i = 0; i < 20; i++) d.Rendas.Add(new RendaMensal { IdDaPessoa = d.Pessoas[0].Id, Mes = mes, Valor = 1, Rotulo = "Linha " + i }); Rejeitar(() => RegrasDoOrcamento.Validar(d)); // 21 linhas
});

// Um arquivo anterior (uma renda por mês, sem Id nem Label) abre: a renda vira "Renda" e ganha identificador.
Teste("Older income records without id and label still open", () =>
{
    var d = Familia(); var no = JsonNode.Parse(JsonSerializer.Serialize(d, ArmazenamentoLocal.OpcoesJson))!; var caminho = Path.GetTempFileName();
    try
    {
        foreach (var i in no["Incomes"]!.AsArray()) { i!.AsObject().Remove("Id"); i.AsObject().Remove("Label"); }
        File.WriteAllText(caminho, no.ToJsonString()); var retorno = ArmazenamentoLocal.Ler(caminho);
        Igual(true, retorno.Rendas.All(i => i.Rotulo == "Renda" && i.Id != Guid.Empty)); Igual(2, retorno.Rendas.Select(i => i.Id).Distinct().Count()); Igual(3000m, MotorFinanceiro.Renda(retorno, retorno.Pessoas[0].Id, mes));
    }
    finally { File.Delete(caminho); }
});

// ---- Categorias personalizadas ----
// Adicionar, usar numa despesa e ver a categoria nas previsões; as padrão continuam disponíveis.
Teste("Custom category can be added and used", () =>
{
    var d = Familia(); MotorDeCategorias.Adicionar(d, "  Pets "); Igual("Pets", d.CategoriasProprias[0]); Igual(9, RegrasDoOrcamento.TodasAsCategorias(d).Count);
    var e = Conta(d, 90); e.Categoria = "Pets"; e.Vencimento = mes.AddMonths(1); d.Despesas.Add(e); RegrasDoOrcamento.Validar(d);
    Igual(true, MotorFinanceiro.Previsoes(d, mes.AddMonths(1)).Any(f => f.Categoria == "Pets" && f.Estimativa == 90m));
});

// Nome vazio, longo, repetido (ignorando caixa), igual a uma padrão e acima de 40 categorias são recusados.
Teste("Custom category names are validated", () =>
{
    var d = Familia(); MotorDeCategorias.Adicionar(d, "Pets");
    Rejeitar(() => MotorDeCategorias.Adicionar(d, "  ")); Rejeitar(() => MotorDeCategorias.Adicionar(d, new string('x', 31))); Rejeitar(() => MotorDeCategorias.Adicionar(d, "pets")); Rejeitar(() => MotorDeCategorias.Adicionar(d, "mercado")); Rejeitar(() => MotorDeCategorias.Adicionar(d, "Ab\nc"));
    for (var i = 1; i < MotorDeCategorias.MaximoDeProprias; i++) MotorDeCategorias.Adicionar(d, "Cat " + i); Rejeitar(() => MotorDeCategorias.Adicionar(d, "Uma a mais"));
    var ruim = Familia(); ruim.CategoriasProprias = ["Moradia"]; Rejeitar(() => RegrasDoOrcamento.Validar(ruim)); ruim.CategoriasProprias = ["A", "a"]; Rejeitar(() => RegrasDoOrcamento.Validar(ruim)); ruim.CategoriasProprias = [" A"]; Rejeitar(() => RegrasDoOrcamento.Validar(ruim));
    var desconhecido = Familia(); desconhecido.Despesas.Add(Conta(desconhecido, 10)); desconhecido.Despesas[0].Categoria = "Inexistente"; Rejeitar(() => RegrasDoOrcamento.Validar(desconhecido));
});

// Renomear atualiza despesas e recorrências; as padrão não podem ser renomeadas nem excluídas.
Teste("Renaming a category updates its expenses and recurrences", () =>
{
    var d = Familia(); MotorDeCategorias.Adicionar(d, "Pets"); var e = Conta(d, 90); e.Categoria = "Pets"; d.Despesas.Add(e);
    var r = Aluguel(d); r.Categoria = "Pets"; d.Recorrencias.Add(r);
    MotorDeCategorias.Renomear(d, "Pets", "Bichos"); Igual("Bichos", d.Despesas[0].Categoria); Igual("Bichos", d.Recorrencias[0].Categoria); RegrasDoOrcamento.Validar(d);
    MotorDeCategorias.Renomear(d, "Bichos", "bichos"); Igual("bichos", d.CategoriasProprias[0]);
    Rejeitar(() => MotorDeCategorias.Renomear(d, "Mercado", "Feira")); Rejeitar(() => MotorDeCategorias.Remover(d, "Mercado")); MotorDeCategorias.Adicionar(d, "Outra"); Rejeitar(() => MotorDeCategorias.Renomear(d, "bichos", "outra"));
});

// Excluir só é possível sem uso; com uso, a mensagem explica o que fazer.
Teste("A category in use cannot be removed", () =>
{
    var d = Familia(); MotorDeCategorias.Adicionar(d, "Pets"); var e = Conta(d, 90); e.Categoria = "Pets"; d.Despesas.Add(e);
    Rejeitar(() => MotorDeCategorias.Remover(d, "Pets")); d.Despesas.Clear(); MotorDeCategorias.Remover(d, "Pets"); Igual(0, d.CategoriasProprias.Count);
});

// As categorias próprias sobrevivem a gravar e ler; arquivos sem o campo continuam abrindo.
Teste("Custom categories persist; older files still open", () =>
{
    var caminho = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid());
    try
    {
        using var armazenamento = new ArmazenamentoLocal(caminho); var d = Familia(); MotorDeCategorias.Adicionar(d, "Pets"); armazenamento.Salvar(d); Igual("Pets", armazenamento.Carregar().CategoriasProprias[0]);
        var no = JsonNode.Parse(JsonSerializer.Serialize(d, ArmazenamentoLocal.OpcoesJson))!; no.AsObject().Remove("CustomCategories"); File.WriteAllText(armazenamento.CaminhoDoArquivo, no.ToJsonString()); Igual(0, armazenamento.Carregar().CategoriasProprias.Count);
    }
    finally { Directory.Delete(caminho, true); }
});

// ---- Metas por categoria ----
// Gasto, meta e nível: 80% é "perto", exatamente 100% ainda é "perto", acima de 100% é "acima".
Teste("Goal progress levels", () =>
{
    var d = Familia(); MotorDeMetas.Definir(d, "Mercado", 1000);
    ProgressoDaMeta Em(decimal gasto) { d.Despesas.Clear(); if (gasto > 0) { var e = Conta(d, gasto); e.Categoria = "Mercado"; d.Despesas.Add(e); } return MotorDeMetas.Progresso(d, mes).Single(); }
    Igual(NivelDaMeta.Dentro, Em(0).Nivel); Igual(NivelDaMeta.Dentro, Em(799.99m).Nivel); Igual(NivelDaMeta.Perto, Em(800).Nivel); Igual(NivelDaMeta.Perto, Em(1000).Nivel); Igual(NivelDaMeta.Acima, Em(1000.01m).Nivel);
    Igual(0.5m, Em(500).Fracao); Igual(500m, Em(500).Gasto);
});

// Só despesas do mês e da categoria contam (pagas ou não); definir de novo troca o valor e limpar remove.
Teste("Goal counts the month and category only", () =>
{
    var d = Familia(); MotorDeMetas.Definir(d, "Mercado", 800); MotorDeMetas.Definir(d, "Mercado", 600); Igual(1, d.Metas.Count); Igual(600m, d.Metas[0].Valor);
    var a = Conta(d, 100); a.Categoria = "Mercado"; var b = Conta(d, 50); b.Categoria = "Mercado"; b.Paga = true; var outra = Conta(d, 70); outra.Categoria = "Lazer"; var maisTarde = Conta(d, 90); maisTarde.Categoria = "Mercado"; maisTarde.Vencimento = mes.AddMonths(1);
    d.Despesas.AddRange([a, b, outra, maisTarde]); Igual(150m, MotorDeMetas.Progresso(d, mes)[0].Gasto);
    MotorDeMetas.Limpar(d, "Mercado"); Igual(0, d.Metas.Count);
});

// Meta inválida (categoria inexistente, valor zero ou duplicada) é recusada; renomear acompanha e excluir a categoria remove a meta.
Teste("Goal validation and category interplay", () =>
{
    var d = Familia(); Rejeitar(() => MotorDeMetas.Definir(d, "Inexistente", 100)); Rejeitar(() => MotorDeMetas.Definir(d, "Mercado", 0));
    d.Metas = [new() { Categoria = "Mercado", Valor = 10 }, new() { Categoria = "MERCADO", Valor = 20 }]; Rejeitar(() => RegrasDoOrcamento.Validar(d));
    d = Familia(); MotorDeCategorias.Adicionar(d, "Pets"); MotorDeMetas.Definir(d, "Pets", 200); MotorDeCategorias.Renomear(d, "Pets", "Bichos"); Igual("Bichos", d.Metas[0].Categoria); RegrasDoOrcamento.Validar(d);
    MotorDeCategorias.Remover(d, "Bichos"); Igual(0, d.Metas.Count);
});

// Metas persistem; arquivos anteriores sem o campo abrem; v1 com metas é recusado.
Teste("Goals persist; older files open; v1 with goals rejected", () =>
{
    var caminho = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid());
    try
    {
        using var armazenamento = new ArmazenamentoLocal(caminho); var d = Familia(); MotorDeMetas.Definir(d, "Mercado", 800); armazenamento.Salvar(d); Igual(800m, armazenamento.Carregar().Metas[0].Valor);
        var no = JsonNode.Parse(JsonSerializer.Serialize(d, ArmazenamentoLocal.OpcoesJson))!; no.AsObject().Remove("Goals"); File.WriteAllText(armazenamento.CaminhoDoArquivo, no.ToJsonString()); Igual(0, armazenamento.Carregar().Metas.Count);
        no = JsonNode.Parse(JsonSerializer.Serialize(d, ArmazenamentoLocal.OpcoesJson))!; no["Version"] = 1; no.AsObject().Remove("Recurrences"); File.WriteAllText(armazenamento.CaminhoDoArquivo, no.ToJsonString()); Rejeitar(() => armazenamento.Carregar());
    }
    finally { Directory.Delete(caminho, true); }
});

// ---- Busca em todos os meses ----
// Sem mês, procura em todos (mais recente primeiro); com mês, só naquele (por vencimento).
Teste("Search across all months or one month", () =>
{
    var d = Familia(); var a = Conta(d, 10); a.Titulo = "Mercado de outubro"; var b = Conta(d, 20); b.Titulo = "Mercado de setembro"; b.Vencimento = mes.AddMonths(-1); var c = Conta(d, 30); c.Titulo = "Cinema"; c.Categoria = "Lazer"; c.Vencimento = mes.AddMonths(2);
    d.Despesas.AddRange([b, a, c]);
    Igual(2, BuscaDeDespesas.Buscar(d, null, "mercado").Count); Igual("Mercado de outubro", BuscaDeDespesas.Buscar(d, null, "mercado")[0].Titulo); // mais recente primeiro
    Igual(1, BuscaDeDespesas.Buscar(d, mes, "mercado").Count); Igual(3, BuscaDeDespesas.Buscar(d, null, "").Count); Igual(1, BuscaDeDespesas.Buscar(d, mes, "").Count);
    Igual("Cinema", BuscaDeDespesas.Buscar(d, null, "")[0].Titulo);
});

// A busca ignora maiúsculas e acentos, procura também na categoria e combina com situação e categoria.
Teste("Search ignores case and accents and combines filters", () =>
{
    var d = Familia(); var a = Conta(d, 10); a.Titulo = "Consulta médica"; a.Categoria = "Saúde"; var b = Conta(d, 20); b.Titulo = "Farmácia"; b.Categoria = "Saúde"; b.Paga = true; var c = Conta(d, 30); c.Titulo = "Pão"; d.Despesas.AddRange([a, b, c]);
    Igual(1, BuscaDeDespesas.Buscar(d, null, "MEDICA").Count); Igual(2, BuscaDeDespesas.Buscar(d, null, "saude").Count); Igual(1, BuscaDeDespesas.Buscar(d, null, "pao").Count);
    Igual(1, BuscaDeDespesas.Buscar(d, null, "saude", paga: true).Count); Igual(1, BuscaDeDespesas.Buscar(d, null, "", paga: false, categoria: "Saúde").Count); Igual(0, BuscaDeDespesas.Buscar(d, null, "xyz").Count);
});

// ---- CSV: leitura, exportação e importação de extrato ----
// Campos entre aspas com separador, aspas duplicadas e quebra de linha; BOM, "sep=" do Excel e linhas vazias.
Teste("CSV parser handles quotes, newlines, BOM and sep line", () =>
{
    var t = Csv.Interpretar("\uFEFFa;b;c\r\n1;\"x;y\";\"say \"\"hi\"\"\"\r\n\r\n2;\"two\nlines\";z\r\n");
    Igual(';', t.Delimitador); Igual(3, t.Linhas.Count); Igual("x;y", t.Linhas[1][1]); Igual("say \"hi\"", t.Linhas[1][2]); Igual("two\nlines", t.Linhas[2][1]);
    Igual(',', Csv.Interpretar("a,b,c\n1,2,3").Delimitador); Igual('\t', Csv.Interpretar("a\tb\n1\t2").Delimitador); Igual(';', Csv.Interpretar("sep=;\r\na;b\r\n1;2").Delimitador); Igual(2, Csv.Interpretar("sep=;\r\na;b\r\n1;2").Linhas.Count);
    Igual(";", Csv.Interpretar("\"a,b\";\"c,d\"\n1;2").Delimitador.ToString()); // vírgulas dentro de aspas não decidem o separador
    Rejeitar(() => Csv.Interpretar("a;\"nunca fecha\n1;2")); Rejeitar(() => Csv.Interpretar("   \r\n  \r\n"));
});

// Valores em vários formatos de extrato; texto, mais de 2 casas e ambiguidades ruins são recusados.
Teste("Statement amounts parse in common formats", () =>
{
    (string Texto, decimal Valor)[] valido = [("1.234,56", 1234.56m), ("1234,56", 1234.56m), ("-1234.56", -1234.56m), ("1,234.56", 1234.56m), ("R$ 12,00", 12m), ("-R$ 12,00", -12m), ("(12,00)", -12m), ("12,00-", -12m), ("1.234", 1234m), ("12,5", 12.5m), ("1.234.567,89", 1234567.89m), ("0,99", 0.99m), ("+7,10", 7.10m), ("3", 3m)];
    foreach (var (texto, valorLido) in valido) { Igual(true, ImportacaoDeExtrato.TentarInterpretarValor(texto, out var interpretadas)); Igual(valorLido, interpretadas); }
    foreach (var ruim in new[] { "", "abc", "12,345,6x", "1,234567", "1.2.3,4,5", "--5", "R$", "12,3456", "-", "1e5" }) Igual(false, ImportacaoDeExtrato.TentarInterpretarValor(ruim, out _));
});

// Datas comuns; a hora depois da data é ignorada; datas impossíveis são recusadas.
Teste("Statement dates parse in common formats", () =>
{
    foreach (var texto in new[] { "05/11/2026", "5/11/2026", "05/11/26", "2026-11-05", "05-11-2026", "05.11.2026", "2026/11/05", "2026-11-05 10:30:00", "2026-11-05T10:30:00", "05/11/2026 08:00" })
    { Igual(true, ImportacaoDeExtrato.TentarInterpretarData(texto, out var d)); Igual(new DateOnly(2026, 11, 5), d); }
    foreach (var ruim in new[] { "", "31/02/2026", "ontem", "13/13/2026", "2026" }) Igual(false, ImportacaoDeExtrato.TentarInterpretarData(ruim, out _));
});

// A exportação usa ; e vírgula decimal, BOM na gravação, e neutraliza fórmulas (=, +, -, @) com apóstrofo.
Teste("CSV export format and formula protection", () =>
{
    var d = Familia(); var a = Conta(d, 1234.5m, ModoDeDivisao.Igual); a.Titulo = "=HYPERLINK(\"http://x\")"; a.Categoria = "Mercado"; a.Paga = true; a.PagaPor = d.Pessoas[0].Id; var b = Conta(d, 10); b.Titulo = "-cmd|calc; \"x\""; b.Vencimento = mes.AddDays(1); d.Despesas.AddRange([a, b]);
    var texto = ExportacaoCsv.Despesas(d); var fileiras = Csv.Interpretar(texto).Linhas;
    Igual(3, fileiras.Count); Igual("Data", fileiras[0][0]); Igual("01/10/2026", fileiras[1][0]); Igual("1234,50", fileiras[1][3]); Igual("Pago", fileiras[1][4]); Igual("Ana", fileiras[1][5]); Igual("Em aberto", fileiras[2][4]);
    Igual(true, fileiras[1][1].StartsWith("'=")); Igual(true, fileiras[2][1].StartsWith("'-")); Igual("-cmd|calc; \"x\"", fileiras[2][1].TrimStart('\''));
    Igual("Ana / Bruno", fileiras[1][6]); Igual(true, fileiras[1][7].Contains("617,25"));
    Igual(2, ExportacaoCsv.Despesas(d, mes.AddMonths(1)).Split("\r\n").Length); // só o cabeçalho (mais a quebra final)
    var caminho = Path.GetTempFileName(); try { ExportacaoCsv.Salvar(caminho, texto); var bruto = File.ReadAllBytes(caminho); Igual(true, bruto[0] == 0xEF && bruto[1] == 0xBB && bruto[2] == 0xBF); } finally { File.Delete(caminho); }
});

// Células comuns não ganham aspas nem apóstrofo; as perigosas e as com separador sim.
Teste("CSV cell escaping", () =>
{
    Igual("Mercado", Csv.Celula("Mercado")); Igual("\"a;b\"", Csv.Celula("a;b")); Igual("\"a\"\"b\"", Csv.Celula("a\"b")); Igual("'=1+1", Csv.Celula("=1+1")); Igual("'@x", Csv.Celula("@x")); Igual("'+1", Csv.Celula("+1")); Igual("\" x\"", Csv.Celula(" x"));
});

// O mapeamento é sugerido pelos nomes do cabeçalho ou, sem cabeçalho, pelo conteúdo das colunas.
Teste("Statement mapping is guessed from headers and from content", () =>
{
    var comCabecalho = ImportacaoDeExtrato.Adivinhar(Csv.Interpretar("Data;Histórico;Valor;Saldo\n05/11/2026;PIX MERCADO;-45,90;1000,00"));
    Igual(0, comCabecalho.ColunaDaData); Igual(1, comCabecalho.ColunaDaDescricao); Igual(2, comCabecalho.ColunaDoValor); Igual(true, comCabecalho.TemCabecalho);
    var semCabecalho = ImportacaoDeExtrato.Adivinhar(Csv.Interpretar("05/11/2026;PADARIA;-12,00\n06/11/2026;FARMACIA;-30,50\n07/11/2026;SALARIO;3000,00"));
    Igual(0, semCabecalho.ColunaDaData); Igual(1, semCabecalho.ColunaDaDescricao); Igual(2, semCabecalho.ColunaDoValor); Igual(false, semCabecalho.TemCabecalho);
    var ingles = ImportacaoDeExtrato.Adivinhar(Csv.Interpretar("Date,Description,Amount\n2026-11-05,Coffee,-3.50"));
    Igual(0, ingles.ColunaDaData); Igual(1, ingles.ColunaDaDescricao); Igual(2, ingles.ColunaDoValor);
});

// Negativos são despesas pagas em "Outros", sem pagador; entradas, zero e linhas ilegíveis ficam de fora (com o número da linha).
Teste("Statement plan creates paid expenses and reports the rest", () =>
{
    var d = Familia(); var tabela = Csv.Interpretar("Data;Descrição;Valor\n05/11/2026;PIX  MERCADO ;-45,90\n06/11/2026;SALÁRIO;3000,00\n07/11/2026;ZERO;0,00\nlixo;X;-1,00\n08/11/2026;Y;abc\n09/11/2026;;-5,00");
    var plano = ImportacaoDeExtrato.Planejar(d, tabela, ImportacaoDeExtrato.Adivinhar(tabela), ModoDeDivisao.Individual, [d.Pessoas[0].Id]);
    Igual(2, plano.Despesas.Count); Igual(2, plano.EntradasIgnoradas); Igual(2, plano.TotalDeErros); Igual("Linha 5: data ilegível", plano.Erros[0]); Igual("Linha 6: valor ilegível", plano.Erros[1]);
    Igual("PIX MERCADO", plano.Despesas[0].Titulo); Igual(45.90m, plano.Despesas[0].Valor); Igual("Outros", plano.Despesas[0].Categoria); Igual(true, plano.Despesas[0].Paga); Igual(true, plano.Despesas[0].PagaPor is null); Igual("Sem descrição", plano.Despesas[1].Titulo);
    d.Despesas.AddRange(plano.Despesas); RegrasDoOrcamento.Validar(d);
});

// Fatura de cartão: positivos são compras e negativos (estornos) são ignorados.
Teste("Statement plan supports card statements (positive is expense)", () =>
{
    var d = Familia(); var tabela = Csv.Interpretar("Data;Descrição;Valor\n05/11/2026;LOJA;100,00\n06/11/2026;ESTORNO;-20,00");
    var plano = ImportacaoDeExtrato.Planejar(d, tabela, ImportacaoDeExtrato.Adivinhar(tabela) with { Sinal = SinalDoValor.PositivoEDespesa }, ModoDeDivisao.Igual, d.Pessoas.Select(p => p.Id).ToList());
    Igual(1, plano.Despesas.Count); Igual(100m, plano.Despesas[0].Valor); Igual(1, plano.EntradasIgnoradas); Igual(2, plano.Despesas[0].Participantes.Count);
});

// Importar o mesmo extrato de novo não duplica nada; duas linhas iguais no arquivo valem como duas compras reais.
Teste("Statement re-import skips what already exists", () =>
{
    var d = Familia(); var tabela = Csv.Interpretar("Data;Descrição;Valor\n05/11/2026;CAFE;-5,00\n05/11/2026;CAFE;-5,00\n06/11/2026;PAO;-7,00");
    var mapa = ImportacaoDeExtrato.Adivinhar(tabela); var eu = new List<Guid> { d.Pessoas[0].Id };
    var primeiro = ImportacaoDeExtrato.Planejar(d, tabela, mapa, ModoDeDivisao.Individual, eu); Igual(3, primeiro.Despesas.Count); Igual(0, primeiro.Duplicadas);
    d.Despesas.AddRange(primeiro.Despesas); var segundo = ImportacaoDeExtrato.Planejar(d, tabela, mapa, ModoDeDivisao.Individual, eu); Igual(0, segundo.Despesas.Count); Igual(3, segundo.Duplicadas);
    d.Despesas.RemoveAt(0); Igual(1, ImportacaoDeExtrato.Planejar(d, tabela, mapa, ModoDeDivisao.Individual, eu).Despesas.Count); // faltava uma: só ela volta
});

// Responsável inválido e arquivos acima dos limites são recusados; a decodificação cai para Latin-1 quando não é UTF-8.
Teste("Statement import limits and encoding", () =>
{
    var d = Familia(); var tabela = Csv.Interpretar("Data;Descrição;Valor\n05/11/2026;X;-1,00");
    Rejeitar(() => ImportacaoDeExtrato.Planejar(d, tabela, ImportacaoDeExtrato.Adivinhar(tabela), ModoDeDivisao.Individual, [])); Rejeitar(() => ImportacaoDeExtrato.Planejar(d, tabela, ImportacaoDeExtrato.Adivinhar(tabela), ModoDeDivisao.Individual, d.Pessoas.Select(p => p.Id).ToList())); Rejeitar(() => ImportacaoDeExtrato.Planejar(d, tabela, ImportacaoDeExtrato.Adivinhar(tabela), ModoDeDivisao.Igual, [Guid.NewGuid()]));
    Rejeitar(() => Csv.Interpretar("a\n" + string.Concat(Enumerable.Repeat("1\n", Csv.MaximoDeLinhas + 1))));
    var caminho = Path.GetTempFileName();
    try
    {
        File.WriteAllBytes(caminho, System.Text.Encoding.Latin1.GetBytes("Data;Descrição;Valor\n05/11/2026;Farmácia;-1,00")); Igual(true, ImportacaoDeExtrato.LerArquivo(caminho).Contains("Farmácia"));
        File.WriteAllBytes(caminho, new System.Text.UTF8Encoding(true).GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes("Descrição")).ToArray()); Igual(true, ImportacaoDeExtrato.LerArquivo(caminho).Contains("Descrição"));
        using (var fluxo = File.OpenWrite(caminho)) fluxo.SetLength(ImportacaoDeExtrato.MaximoDeBytes + 1); Rejeitar(() => ImportacaoDeExtrato.LerArquivo(caminho));
    }
    finally { File.Delete(caminho); }
});

// ---- Windows e Linux ----
// A pasta de dados padrão fica na pasta local do usuário em qualquer sistema (%LOCALAPPDATA% no Windows, ~/.local/share no Linux).
Teste("Default data folder is per-user and portable", () =>
{
    var esperado = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BankManager");
    Igual(esperado, ArmazenamentoLocal.PastaPadrao(null)); Igual(esperado, ArmazenamentoLocal.PastaPadrao("  ")); Igual("/custom", ArmazenamentoLocal.PastaPadrao("/custom"));
    Igual(true, Path.IsPathRooted(esperado) && !esperado.StartsWith(AppContext.BaseDirectory)); // nunca dentro da pasta do programa: atualizar ou desinstalar não toca nos dados
});

// Pastas com espaços e acentos (comuns em nomes de usuário do Windows) funcionam para dados, configurações e exportação.
Teste("Data folders with spaces and accents work", () =>
{
    var caminho = Path.Combine(Path.GetTempPath(), "bankmanager test " + Guid.NewGuid() + " João çã");
    try
    {
        using var armazenamento = new ArmazenamentoLocal(caminho); var d = Familia(); d.Despesas.Add(Conta(d, 800)); armazenamento.Salvar(d); ArmazenamentoDePreferencias.Salvar(caminho, new Preferencias { Tema = PreferenciaDeTema.Escuro });
        Igual(800m, armazenamento.Carregar().Despesas[0].Valor); Igual(PreferenciaDeTema.Escuro, ArmazenamentoDePreferencias.Carregar(caminho).Tema);
        var csv = Path.Combine(caminho, "exportação.csv"); ExportacaoCsv.Salvar(csv, ExportacaoCsv.Despesas(d)); Igual(true, ImportacaoDeExtrato.LerArquivo(csv).Contains("Descrição"));
    }
    finally { Directory.Delete(caminho, true); }
});

// Gráfico da visão geral: seis meses terminando no selecionado, do mais antigo ao mais recente, com zero nos meses vazios.
Teste("Monthly totals cover the last N months including empty ones", () =>
{
    var d = Familia(); var paga = Conta(d, 300); paga.Paga = true; d.Despesas.Add(paga); d.Despesas.Add(Conta(d, 200.50m));
    var antigo = Conta(d, 90); antigo.Vencimento = mes.AddMonths(-2).AddDays(14); d.Despesas.Add(antigo);
    var fora = Conta(d, 999); fora.Vencimento = mes.AddMonths(-6); d.Despesas.Add(fora); var futura = Conta(d, 50); futura.Vencimento = mes.AddMonths(1); d.Despesas.Add(futura);
    var totais = MotorFinanceiro.TotaisMensais(d, mes.AddDays(20), 6);
    Igual(6, totais.Count); Igual(mes.AddMonths(-5), totais[0].Mes); Igual(mes, totais[5].Mes);
    Igual(500.50m, totais[5].Total); Igual(90m, totais[3].Total); Igual(0m, totais[0].Total); Igual(590.50m, totais.Sum(t => t.Total));
    Igual(1, MotorFinanceiro.TotaisMensais(new DadosDoOrcamento(), mes, 1).Count);
    try { MotorFinanceiro.TotaisMensais(d, mes, 0); throw new Exception("Expected rejection"); } catch (ArgumentOutOfRangeException) { }
});

// Últimas movimentações: rendas do mês (dia 1) e despesas pagas do mês, mais recentes primeiro, com limite.
Teste("Recent movements list incomes and paid expenses, newest first", () =>
{
    var d = Familia(3000, 0); d.Rendas.Add(new() { IdDaPessoa = d.Pessoas[0].Id, Mes = mes, Valor = 500, Rotulo = "Extra" });
    d.Rendas.Add(new() { IdDaPessoa = d.Pessoas[0].Id, Mes = mes.AddMonths(1), Valor = 7000 });
    Despesa Paga(string titulo, int dia, decimal valor, bool paga = true) { var e = Conta(d, valor); e.Titulo = titulo; e.Vencimento = mes.AddDays(dia - 1); e.Paga = paga; d.Despesas.Add(e); return e; }
    Paga("Aluguel", 5, 1800); Paga("Energia", 12, 214.90m, paga: false); Paga("Internet", 12, 119.90m); Paga("Água", 12, 80); Paga("Dia um", 1, 10);
    var outra = Paga("Outro mês", 3, 40); outra.Vencimento = mes.AddMonths(-1);
    var todas = MotorFinanceiro.MovimentacoesRecentes(d, mes, 10);
    // Dia 12 (Água antes de Internet, pela descrição), dia 5 e, no dia 1, a despesa antes das rendas na ordem de cadastro.
    Igual("Água,Internet,Aluguel,Dia um,Renda,Extra", string.Join(",", todas.Select(m => m.Titulo)));
    Igual(true, todas[4].Entrada && todas[4].Data == mes && todas[4].Categoria == "Renda de Ana" && todas[4].Valor == 3000m); Igual(false, todas[0].Entrada);
    Igual(2, MotorFinanceiro.MovimentacoesRecentes(d, mes, 2).Count); Igual(0, MotorFinanceiro.MovimentacoesRecentes(new DadosDoOrcamento(), mes).Count);
    // Mesma entrada, mesma saída: a ordem não muda entre chamadas.
    Igual(true, todas.SequenceEqual(MotorFinanceiro.MovimentacoesRecentes(d, mes, 10)));
});

// Arquivos REAIS gravados pelas versões anteriores (pasta Fixtures): o v1 pelo código da branch main, o v2 pelo BankManager 2.1.
// Cada um abre sem perda, passa para a versão 3 só em memória e, ao salvar, o arquivo original vira a cópia anterior, byte a byte.
Teste("Real version 1 and 2 files migrate to version 3 in a chain", () =>
{
    foreach (var (nome, versao) in new[] { ("financas-v1.json", 1), ("financas-v2.json", 2) })
    {
        var caminho = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid()); Directory.CreateDirectory(caminho);
        try
        {
            var original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", nome)); File.WriteAllBytes(Path.Combine(caminho, "financas.json"), original);
            var antes = JsonNode.Parse(original)!; Igual(versao, (int)antes["Version"]!);
            using var armazenamento = new ArmazenamentoLocal(caminho); var carregado = armazenamento.Carregar();
            Igual(3, carregado.Versao); Igual(2, carregado.Pessoas.Count); Igual(antes["Expenses"]!.AsArray().Count, carregado.Despesas.Count); Igual(antes["Incomes"]!.AsArray().Count, carregado.Rendas.Count);
            Igual(antes["Expenses"]!.AsArray().Sum(e => (decimal)e!["Amount"]!), carregado.Despesas.Sum(e => e.Valor));
            // Ler não altera o arquivo em disco.
            Igual(true, original.SequenceEqual(File.ReadAllBytes(armazenamento.CaminhoDoArquivo)));
            // Despesas pagas antes da versão 3 ficam sem data de pagamento: a referência é o vencimento.
            var aluguel = carregado.Despesas.Single(e => e.Titulo == "Aluguel"); Igual(true, aluguel.Paga && aluguel.DataDoPagamento is null && aluguel.IdDeOrigem is null); Igual(aluguel.Vencimento, aluguel.DataDeReferencia);
            Igual(3, carregado.Despesas.Count(e => e.Titulo == "Notebook" && e.TotalDeParcelas == 3)); Igual(2m, carregado.Despesas.Single(e => e.Titulo == "Mercado").Itens[0].Quantidade);
            if (versao == 2)
            {
                Igual(true, aluguel.PagaPor == carregado.Pessoas[0].Id && carregado.CategoriasProprias.SequenceEqual(["Pets"]) && carregado.Metas.Single().Valor == 800m && carregado.Acertos.Single().Valor == 100m);
                Igual(1, carregado.Recorrencias.Count); Igual(true, carregado.Despesas.Count(e => e.IdDaRecorrencia is not null) > 1); Igual(3500m, MotorFinanceiro.Renda(carregado, carregado.Pessoas[0].Id, mes));
            }
            else Igual(3000m, MotorFinanceiro.Renda(carregado, carregado.Pessoas[0].Id, mes));
            armazenamento.Salvar(carregado);
            Igual(true, original.SequenceEqual(File.ReadAllBytes(armazenamento.CaminhoDaCopia))); Igual(3, (int)JsonNode.Parse(File.ReadAllText(armazenamento.CaminhoDoArquivo))!["Version"]!);
            Igual(carregado.Despesas.Sum(e => e.Valor), armazenamento.Carregar().Despesas.Sum(e => e.Valor));
            Igual(0, Directory.GetFiles(caminho, "*.tmp").Length);
        }
        finally { Directory.Delete(caminho, true); }
    }
});

// Um arquivo que diz ser v2 (ou v1) mas traz data de pagamento está adulterado; uma versão maior que a do aplicativo é recusada.
Teste("Older versions with version 3 data and newer versions are rejected", () =>
{
    var caminho = Path.GetTempFileName();
    try
    {
        var d = Familia(); var conta = Conta(d, 800); conta.Paga = true; conta.DataDoPagamento = mes.AddDays(3); d.Despesas.Add(conta);
        foreach (var versao in new[] { 1, 2, 4 })
        {
            var no = JsonNode.Parse(JsonSerializer.Serialize(d, ArmazenamentoLocal.OpcoesJson))!; no["Version"] = versao; File.WriteAllText(caminho, no.ToJsonString()); Rejeitar(() => ArmazenamentoLocal.Ler(caminho));
        }
        // O mesmo conteúdo na versão 3 é válido e a data de pagamento sobrevive à gravação.
        ArmazenamentoLocal.Exportar(caminho, d); Igual(mes.AddDays(3), ArmazenamentoLocal.Ler(caminho).Despesas[0].DataDoPagamento);
    }
    finally { File.Delete(caminho); }
});

// A data de pagamento só existe em despesa paga, dentro do intervalo de datas aceito; parcelas novas nascem sem ela.
Teste("Paid date requires a paid expense and a valid date", () =>
{
    var d = Familia(); var conta = Conta(d, 100); conta.DataDoPagamento = mes; d.Despesas.Add(conta); Rejeitar(() => RegrasDoOrcamento.Validar(d));
    conta.Paga = true; RegrasDoOrcamento.Validar(d);
    conta.DataDoPagamento = new DateOnly(1999, 12, 31); Rejeitar(() => RegrasDoOrcamento.Validar(d));
    conta.DataDoPagamento = null; RegrasDoOrcamento.Validar(d); Igual(conta.Vencimento, conta.DataDeReferencia);
    var fonte = Conta(d, 300); fonte.Paga = true; fonte.DataDoPagamento = mes; fonte.PagaPor = d.Pessoas[0].Id;
    Igual(true, RegrasDoOrcamento.Parcelar(fonte, 3).All(e => !e.Paga && e.DataDoPagamento is null && e.PagaPor is null)); Igual(mes, RegrasDoOrcamento.Parcelar(fonte, 1)[0].DataDoPagamento);
});

// Movimentações usam a data do pagamento: a despesa aparece no mês em que foi paga, não no do vencimento.
Teste("Recent movements follow the paid date into its month", () =>
{
    var d = Familia(0, 0);
    Despesa Paga(string titulo, DateOnly vencimento, DateOnly? pagaEm) { var e = Conta(d, 100); e.Titulo = titulo; e.Vencimento = vencimento; e.Paga = true; e.DataDoPagamento = pagaEm; d.Despesas.Add(e); return e; }
    Paga("Venceu em outubro, paga em novembro", mes.AddDays(27), mes.AddMonths(1).AddDays(2));
    Paga("Vence em novembro, paga adiantada", mes.AddMonths(1).AddDays(9), mes.AddDays(29));
    Paga("Sem data de pagamento", mes.AddDays(4), null); Paga("Paga no dia", mes.AddDays(9), mes.AddDays(9));
    Igual("Vence em novembro, paga adiantada,Paga no dia,Sem data de pagamento", string.Join(",", MotorFinanceiro.MovimentacoesRecentes(d, mes).Select(m => m.Titulo)));
    Igual(mes.AddDays(29), MotorFinanceiro.MovimentacoesRecentes(d, mes)[0].Data); Igual(mes.AddDays(4), MotorFinanceiro.MovimentacoesRecentes(d, mes)[2].Data);
    Igual("Venceu em outubro, paga em novembro", string.Join(",", MotorFinanceiro.MovimentacoesRecentes(d, mes.AddMonths(1)).Select(m => m.Titulo)));
    // O total do mês (gráfico e saldo) continua seguindo o vencimento.
    Igual(300m, MotorFinanceiro.TotaisMensais(d, mes, 1)[0].Total);
});

// Extrato importado: a data da própria linha vira a data de pagamento; a planilha exportada traz a coluna "Pago em".
Teste("Statement import sets the paid date and CSV export shows it", () =>
{
    var d = Familia(); var tabela = Csv.Interpretar("Data;Histórico;Valor\r\n02/10/2026;PADARIA;-12,50\r\n");
    var plano = ImportacaoDeExtrato.Planejar(d, tabela, new MapeamentoDeImportacao(0, 1, 2, true, SinalDoValor.NegativoEDespesa), ModoDeDivisao.Individual, [d.Pessoas[0].Id]);
    Igual(new DateOnly(2026, 10, 2), plano.Despesas.Single().DataDoPagamento); d.Despesas.AddRange(plano.Despesas); RegrasDoOrcamento.Validar(d);
    var fileiras = Csv.Interpretar(ExportacaoCsv.Despesas(d)).Linhas; Igual("Pago em", fileiras[0][^1]); Igual("02/10/2026", fileiras[1][^1]);
});

// ===== Compartilhamento de despesas =====
// Base de quem envia: Ana e Bruno, com renda, meta, acerto, uma compra parcelada, uma categoria própria e despesas pagas.
DadosDoOrcamento Remetente()
{
    var d = Familia(); d.CategoriasProprias.Add("Pets"); MotorDeMetas.Definir(d, "Mercado", 900);
    var mercado = Conta(d, 800); mercado.Paga = true; mercado.PagaPor = d.Pessoas[0].Id; mercado.DataDoPagamento = mes.AddDays(2); d.Despesas.Add(mercado);
    d.Despesas.Add(new Despesa { Titulo = "Ração", Categoria = "Pets", Valor = 89.90m, Vencimento = mes.AddDays(8), Divisao = ModoDeDivisao.Individual, Participantes = [d.Pessoas[1].Id] });
    d.Despesas.Add(new Despesa { Titulo = "Feira", Categoria = "Mercado", Valor = 51m, Vencimento = mes.AddDays(9), Divisao = ModoDeDivisao.Igual, Participantes = d.Pessoas.Select(p => p.Id).ToList(), Itens = [new ItemDaDespesa { Nome = "Arroz", Quantidade = 2, PrecoUnitario = 25.50m }] });
    d.Despesas.AddRange(RegrasDoOrcamento.Parcelar(new Despesa { Titulo = "Notebook", Categoria = "Educação", Valor = 1000m, Vencimento = mes.AddDays(14), Divisao = ModoDeDivisao.Igual, Participantes = d.Pessoas.Select(p => p.Id).ToList() }, 3));
    d.Despesas.Add(new Despesa { Titulo = "Segredo", Categoria = "Outros", Valor = 999m, Vencimento = mes, Divisao = ModoDeDivisao.Individual, Participantes = [d.Pessoas[0].Id] });
    d.Acertos.Add(new Acerto { IdDoPagador = d.Pessoas[1].Id, IdDoRecebedor = d.Pessoas[0].Id, Valor = 50, Data = mes.AddDays(5) });
    RegrasDoOrcamento.Validar(d); return d;
}
// Base de quem recebe: Carla, com renda, meta, uma despesa e um acerto próprios, que nunca podem ser tocados.
DadosDoOrcamento Recebedor()
{
    var d = new DadosDoOrcamento { Pessoas = [new() { Nome = "Carla" }, new() { Nome = "Érica" }] };
    d.Rendas.Add(new() { IdDaPessoa = d.Pessoas[0].Id, Mes = mes, Valor = 2500 }); MotorDeMetas.Definir(d, "Lazer", 200);
    d.Despesas.Add(new Despesa { Titulo = "Cinema", Categoria = "Lazer", Valor = 60, Vencimento = mes.AddDays(3), Divisao = ModoDeDivisao.Individual, Participantes = [d.Pessoas[0].Id] });
    d.Acertos.Add(new Acerto { IdDoPagador = d.Pessoas[1].Id, IdDoRecebedor = d.Pessoas[0].Id, Valor = 10, Data = mes });
    RegrasDoOrcamento.Validar(d); return d;
}
string Retrato(DadosDoOrcamento d) => JsonSerializer.Serialize(d, ArmazenamentoLocal.OpcoesJson);

// O arquivo leva só as despesas escolhidas, com pessoas por nome; nunca rendas, metas, acertos ou despesas não selecionadas.
Teste("Share file contains only the selected expenses", () =>
{
    var d = Remetente(); var escolhidas = d.Despesas.Where(e => e.Titulo is "Mercado" or "Ração").Select(e => e.Id).ToList();
    var arquivo = CompartilhamentoDeDespesas.Montar(d, escolhidas, incluirPagamento: false);
    Igual("share", arquivo.Tipo); Igual("BankManager", arquivo.Aplicativo); Igual(2, arquivo.Despesas.Count); Igual(true, arquivo.Pessoas.SequenceEqual(["Ana", "Bruno"])); Igual(true, arquivo.CategoriasProprias.SequenceEqual(["Pets"]));
    // Sem a opção, tudo sai em aberto, sem data nem pagador.
    Igual(true, arquivo.Despesas.All(e => !e.Paga && e.DataDoPagamento is null && e.PagaPor is null));
    var json = JsonSerializer.Serialize(arquivo, ArmazenamentoLocal.OpcoesJson);
    foreach (var proibido in new[] { "Segredo", "Notebook", "Incomes", "Goals", "Settlements", "3000", "Recurrences", d.Pessoas[0].Id.ToString() }) Igual(false, json.Contains(proibido));
    // Com a opção, a situação, a data e quem pagou acompanham; só quem é citado entra na lista de pessoas.
    var paga = CompartilhamentoDeDespesas.Montar(d, [d.Despesas.First(e => e.Titulo == "Mercado").Id], incluirPagamento: true).Despesas.Single();
    Igual(true, paga.Paga && paga.PagaPor == "Ana" && paga.DataDoPagamento == mes.AddDays(2));
    Igual(true, CompartilhamentoDeDespesas.Montar(d, [d.Despesas.First(e => e.Titulo == "Ração").Id], false).Pessoas.SequenceEqual(["Bruno"]));
    Rejeitar(() => CompartilhamentoDeDespesas.Montar(d, [], false)); Rejeitar(() => CompartilhamentoDeDespesas.Montar(d, [Guid.NewGuid()], false));
});

// Importar ADICIONA: mapeia pessoas (existente, nova, ignorada), cria categoria, e não toca em nada que já existia.
Teste("Import adds expenses with mapped people and categories, touching nothing else", () =>
{
    var remetente = Remetente(); var recebedor = Recebedor(); var antes = Retrato(recebedor);
    var arquivo = CompartilhamentoDeDespesas.Montar(remetente, remetente.Despesas.Where(e => e.Titulo is "Mercado" or "Ração" or "Feira").Select(e => e.Id).ToList(), incluirPagamento: true);
    var sugestao = CompartilhamentoDeDespesas.Sugerir(recebedor, arquivo);
    Igual(AcaoDaPessoa.Criar, sugestao.Pessoas["Ana"].Acao); Igual(true, sugestao.CriarCategorias["Pets"]);
    // Ana → Carla (existente), Bruno → cadastro novo.
    var mapeamento = sugestao with { Pessoas = new() { ["Ana"] = new(AcaoDaPessoa.Existente, recebedor.Pessoas[0].Id), ["Bruno"] = new(AcaoDaPessoa.Criar) } };
    var (proximo, resultado) = CompartilhamentoDeDespesas.Importar(recebedor, arquivo, mapeamento);
    Igual(antes, Retrato(recebedor)); // a base original não muda: a importação trabalha em uma cópia
    Igual(3, resultado.Adicionadas.Count); Igual(0, resultado.Duplicadas); Igual(0, resultado.Erros.Count); Igual(true, resultado.PessoasNovas.SequenceEqual(["Bruno"])); Igual(true, resultado.CategoriasNovas.SequenceEqual(["Pets"]));
    Igual(4, proximo.Despesas.Count); Igual(3, proximo.Pessoas.Count); Igual(true, proximo.CategoriasProprias.SequenceEqual(["Pets"]));
    // Tudo o que existia continua igual: rendas, metas, acertos e a despesa antiga.
    Igual(Retrato(new DadosDoOrcamento { Rendas = recebedor.Rendas, Metas = recebedor.Metas, Acertos = recebedor.Acertos, Despesas = [recebedor.Despesas[0]] }), Retrato(new DadosDoOrcamento { Rendas = proximo.Rendas, Metas = proximo.Metas, Acertos = proximo.Acertos, Despesas = [proximo.Despesas[0]] }));
    var mercado = proximo.Despesas.Single(e => e.Titulo == "Mercado"); var original = remetente.Despesas.First(e => e.Titulo == "Mercado");
    Igual(true, mercado.Id != original.Id && mercado.IdDeOrigem == original.Id && mercado.Paga && mercado.PagaPor == recebedor.Pessoas[0].Id && mercado.DataDoPagamento == mes.AddDays(2) && mercado.Participantes.Count == 2);
    Igual(1, resultado.Proporcionais); Igual(800m, MotorFinanceiro.Ratear(proximo, mercado).Valores.Values.Sum()); // o rateio fecha com as rendas locais
    Igual("Pets", proximo.Despesas.Single(e => e.Titulo == "Ração").Categoria); Igual(2m, proximo.Despesas.Single(e => e.Titulo == "Feira").Itens[0].Quantidade);
    // Nomes iguais sem acento ou maiúsculas são sugeridos como a mesma pessoa.
    var acentos = CompartilhamentoDeDespesas.Montar(remetente, [remetente.Despesas.First(e => e.Titulo == "Ração").Id], false) with { Pessoas = ["erica"] }; acentos.Despesas[0].Participantes = ["erica"];
    Igual(recebedor.Pessoas[1].Id, CompartilhamentoDeDespesas.Sugerir(recebedor, acentos).Pessoas["erica"].IdDaPessoa);
});

// Pessoa ignorada sai da despesa; se ninguém sobra, é preciso escolher quem assume. Categoria não criada vira "Outros"; opção de divisão igual.
Teste("Ignored people, fallback, Outros and equal split options", () =>
{
    var remetente = Remetente(); var recebedor = Recebedor(); var antes = Retrato(recebedor);
    var arquivo = CompartilhamentoDeDespesas.Montar(remetente, remetente.Despesas.Where(e => e.Titulo is "Mercado" or "Ração").Select(e => e.Id).ToList(), incluirPagamento: true);
    var mapeamento = new MapeamentoDoCompartilhamento { Pessoas = new() { ["Ana"] = new(AcaoDaPessoa.Existente, recebedor.Pessoas[0].Id), ["Bruno"] = new(AcaoDaPessoa.Ignorar) }, IgualEmVezDeProporcional = true };
    // "Ração" era só do Bruno: sem escolher quem assume, nada é importado e a base fica intacta.
    Rejeitar(() => CompartilhamentoDeDespesas.Importar(recebedor, arquivo, mapeamento)); Igual(antes, Retrato(recebedor));
    var (proximo, resultado) = CompartilhamentoDeDespesas.Importar(recebedor, arquivo, mapeamento with { ResponsavelPadrao = recebedor.Pessoas[1].Id });
    Igual(2, resultado.Adicionadas.Count); Igual(0, resultado.PessoasNovas.Count); Igual(0, resultado.CategoriasNovas.Count); Igual(2, proximo.Pessoas.Count);
    var animal = proximo.Despesas.Single(e => e.Titulo == "Ração"); Igual("Outros", animal.Categoria); Igual(true, animal.Participantes.SequenceEqual([recebedor.Pessoas[1].Id]) && animal.Divisao == ModoDeDivisao.Individual);
    var mercado = proximo.Despesas.Single(e => e.Titulo == "Mercado"); Igual(true, mercado.Divisao == ModoDeDivisao.Igual && mercado.Participantes.SequenceEqual([recebedor.Pessoas[0].Id])); Igual(0, resultado.Proporcionais);
    // Escolha ausente para um nome é um erro claro, não um palpite.
    Rejeitar(() => CompartilhamentoDeDespesas.Importar(recebedor, arquivo, new MapeamentoDoCompartilhamento { Pessoas = new() { ["Ana"] = new(AcaoDaPessoa.Criar) } }));
    // Limite de 40 categorias próprias: criar além dele falha por inteiro; mandar para "Outros" funciona.
    var cheia = Recebedor(); for (var i = 0; i < MotorDeCategorias.MaximoDeProprias; i++) cheia.CategoriasProprias.Add("Cat " + i);
    Igual(false, CompartilhamentoDeDespesas.Sugerir(cheia, arquivo).CriarCategorias["Pets"]);
    var criar = new MapeamentoDoCompartilhamento { Pessoas = new() { ["Ana"] = new(AcaoDaPessoa.Criar), ["Bruno"] = new(AcaoDaPessoa.Criar) }, CriarCategorias = new() { ["Pets"] = true } };
    Rejeitar(() => CompartilhamentoDeDespesas.Importar(cheia, arquivo, criar)); Igual(2, CompartilhamentoDeDespesas.Importar(cheia, arquivo, criar with { CriarCategorias = [] }).Result.Adicionadas.Count);
});

// Importar o mesmo arquivo duas vezes não duplica; "importar mesmo assim" cria cópias com novo Id; parcelas mantêm o vínculo.
Teste("Duplicates are skipped by origin id and installments stay linked", () =>
{
    var remetente = Remetente(); var recebedor = Recebedor();
    var arquivo = CompartilhamentoDeDespesas.Montar(remetente, remetente.Despesas.Where(e => e.Titulo is "Notebook" or "Feira").Select(e => e.Id).ToList(), incluirPagamento: false);
    var mapeamento = CompartilhamentoDeDespesas.Sugerir(recebedor, arquivo);
    var (primeiro, adicionadas) = CompartilhamentoDeDespesas.Importar(recebedor, arquivo, mapeamento); Igual(4, adicionadas.Adicionadas.Count);
    var partes = primeiro.Despesas.Where(e => e.Titulo == "Notebook").OrderBy(e => e.NumeroDaParcela).ToList();
    Igual(true, partes.Count == 3 && partes.Select(e => e.GrupoDeParcelas).Distinct().Count() == 1 && partes[0].GrupoDeParcelas is not null && partes[0].GrupoDeParcelas != remetente.Despesas.First(e => e.Titulo == "Notebook").GrupoDeParcelas);
    Igual(true, partes.Select(e => e.NumeroDaParcela).SequenceEqual([1, 2, 3]) && partes.Sum(e => e.Valor) == 1000m && primeiro.Despesas.Single(e => e.Titulo == "Feira").GrupoDeParcelas is null);
    // Segunda importação: tudo duplicado, nada adicionado. As pessoas criadas na primeira vez são reaproveitadas.
    var (segundo, deNovo) = CompartilhamentoDeDespesas.Importar(primeiro, arquivo, CompartilhamentoDeDespesas.Sugerir(primeiro, arquivo)); Igual(0, deNovo.Adicionadas.Count); Igual(4, deNovo.Duplicadas); Igual(Retrato(primeiro), Retrato(segundo));
    // Importar mesmo assim: novas despesas, novos Ids, mesma origem; uma terceira tentativa continua reconhecendo as duplicadas.
    var (forcada, copia) = CompartilhamentoDeDespesas.Importar(primeiro, arquivo, CompartilhamentoDeDespesas.Sugerir(primeiro, arquivo) with { ImportarDuplicadas = true });
    Igual(4, copia.Adicionadas.Count); Igual(primeiro.Despesas.Count + 4, forcada.Despesas.Count); Igual(forcada.Despesas.Count, forcada.Despesas.Select(e => e.Id).Distinct().Count());
    Igual(4, CompartilhamentoDeDespesas.Importar(forcada, arquivo, CompartilhamentoDeDespesas.Sugerir(forcada, arquivo)).Result.Duplicadas);
    // Quem enviou importa o próprio arquivo: as despesas já existem aqui pelo Id.
    Igual(4, CompartilhamentoDeDespesas.Importar(remetente, arquivo, CompartilhamentoDeDespesas.Sugerir(remetente, arquivo)).Result.Duplicadas);
    // Reexportar uma despesa importada preserva a origem inicial.
    Igual(remetente.Despesas.First(e => e.Titulo == "Feira").Id, CompartilhamentoDeDespesas.Montar(primeiro, [primeiro.Despesas.Single(e => e.Titulo == "Feira").Id], false).Despesas[0].IdDeOrigem);
});

// Erros por despesa: a linha ruim é apontada e pulada, as boas entram; pessoas criadas só para a linha ruim são desfeitas.
Teste("Per-expense errors are reported and skipped", () =>
{
    var remetente = Remetente(); var recebedor = Recebedor();
    var arquivo = CompartilhamentoDeDespesas.Montar(remetente, remetente.Despesas.Where(e => e.Titulo is "Mercado" or "Ração").Select(e => e.Id).ToList(), false);
    arquivo.Despesas.Single(e => e.Titulo == "Ração").Valor = -5; // arquivo adulterado depois de gerado
    var (proximo, resultado) = CompartilhamentoDeDespesas.Importar(recebedor, arquivo, CompartilhamentoDeDespesas.Sugerir(recebedor, arquivo));
    Igual(1, resultado.Adicionadas.Count); Igual(1, resultado.Erros.Count); Igual(true, resultado.Erros[0].Contains("Ração")); Igual(0, resultado.CategoriasNovas.Count); Igual(0, proximo.CategoriasProprias.Count);
    Igual(true, resultado.PessoasNovas.OrderBy(n => n).SequenceEqual(["Ana", "Bruno"])); // os dois participam de "Mercado"
    arquivo.Despesas.Single(e => e.Titulo == "Mercado").Vencimento = new DateOnly(1800, 1, 1);
    var nenhum = CompartilhamentoDeDespesas.Importar(recebedor, arquivo, CompartilhamentoDeDespesas.Sugerir(recebedor, arquivo)); Igual(0, nenhum.Result.Adicionadas.Count); Igual(2, nenhum.Result.Erros.Count); Igual(2, nenhum.Next.Pessoas.Count);
});

// A prévia recebe uma linha por despesa do arquivo; "importar mesmo assim" vale por despesa; a versão do app viaja no arquivo.
Teste("Preview lines, per-expense import anyway and app version", () =>
{
    var remetente = Remetente(); var recebedor = Recebedor();
    var arquivo = CompartilhamentoDeDespesas.Montar(remetente, remetente.Despesas.Where(e => e.Titulo is "Mercado" or "Ração" or "Feira").Select(e => e.Id).ToList(), false, "2.2");
    Igual("2.2", arquivo.VersaoDoAplicativo); arquivo.VersaoDoAplicativo = "2.2<script>"; Rejeitar(() => CompartilhamentoDeDespesas.Validar(arquivo)); arquivo.VersaoDoAplicativo = "2.2";
    var (primeiro, _) = CompartilhamentoDeDespesas.Importar(recebedor, arquivo, CompartilhamentoDeDespesas.Sugerir(recebedor, arquivo));
    var feira = arquivo.Despesas.Single(e => e.Titulo == "Feira").IdDeOrigem;
    // Tudo já existe: três linhas puladas, na ordem do arquivo.
    var todas = CompartilhamentoDeDespesas.Importar(primeiro, arquivo, CompartilhamentoDeDespesas.Sugerir(primeiro, arquivo)).Result;
    Igual(true, todas.Linhas.Count == 3 && todas.Linhas.All(l => l.Situacao == SituacaoDaLinha.Pulada && l.JaExiste) && todas.Linhas.Select(l => l.Origem.Titulo).SequenceEqual(arquivo.Despesas.Select(e => e.Titulo)));
    // Só a "Feira" é importada mesmo assim: uma adicionada (marcada como já existente), duas puladas.
    var uma = CompartilhamentoDeDespesas.Importar(primeiro, arquivo, CompartilhamentoDeDespesas.Sugerir(primeiro, arquivo) with { ImportarMesmoAssim = new HashSet<Guid> { feira } });
    Igual(1, uma.Result.Adicionadas.Count); Igual(2, uma.Result.Duplicadas); Igual(primeiro.Despesas.Count + 1, uma.Next.Despesas.Count);
    var linha = uma.Result.Linhas.Single(l => l.Situacao == SituacaoDaLinha.Adicionada); Igual(true, linha.JaExiste && linha.Despesa!.Titulo == "Feira" && linha.Despesa.Id != feira);
    // Uma linha com erro aparece como erro, com a explicação.
    arquivo.Despesas[0].Valor = 0; var quebrado = CompartilhamentoDeDespesas.Importar(recebedor, arquivo, CompartilhamentoDeDespesas.Sugerir(recebedor, arquivo)).Result.Linhas[0]; Igual(true, quebrado.Situacao == SituacaoDaLinha.Erro && quebrado.Erro is not null);
});

// Duas pessoas do arquivo não podem virar a mesma pessoa aqui se dividem uma despesa: a divisão mudaria sem aviso.
Teste("Two file people cannot map to the same person when they share an expense", () =>
{
    var remetente = Remetente(); var recebedor = Recebedor(); var antes = Retrato(recebedor); var carla = recebedor.Pessoas[0].Id;
    var ambos = new MapeamentoDoCompartilhamento { Pessoas = new() { ["Ana"] = new(AcaoDaPessoa.Existente, carla), ["Bruno"] = new(AcaoDaPessoa.Existente, carla) } };
    var contaCompartilhada = CompartilhamentoDeDespesas.Montar(remetente, [remetente.Despesas.First(e => e.Titulo == "Mercado").Id], false);
    try { CompartilhamentoDeDespesas.Importar(recebedor, contaCompartilhada, ambos); throw new Exception("Expected rejection"); } catch (ErroDeOrcamento ex) { Igual(true, ex.Message.Contains("Ana") && ex.Message.Contains("Bruno")); }
    Igual(antes, Retrato(recebedor));
    // Se eles não dividem nenhuma despesa do arquivo, ligar os dois à mesma pessoa é aceito.
    var separadas = CompartilhamentoDeDespesas.Montar(remetente, remetente.Despesas.Where(e => e.Titulo is "Ração" or "Segredo").Select(e => e.Id).ToList(), false);
    Igual(2, CompartilhamentoDeDespesas.Importar(recebedor, separadas, ambos).Result.Adicionadas.Count);
});

// Tipos de arquivo não se misturam, e o arquivo de compartilhamento tem as mesmas proteções do backup.
Teste("Share and backup files are different kinds and both are guarded", () =>
{
    var pasta = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid()); Directory.CreateDirectory(pasta);
    try
    {
        var remetente = Remetente(); var compartilhar = Path.Combine(pasta, "despesas" + ArquivoDeCompartilhamento.Extensao); var backup = Path.Combine(pasta, "backup.json");
        CompartilhamentoDeDespesas.Salvar(compartilhar, CompartilhamentoDeDespesas.Montar(remetente, [remetente.Despesas[0].Id], false)); ArmazenamentoLocal.Exportar(backup, remetente);
        Igual(1, CompartilhamentoDeDespesas.Ler(compartilhar).Despesas.Count); Igual(0, Directory.GetFiles(pasta, "*.tmp").Length);
        // Backup como compartilhamento e compartilhamento como backup: recusados, com mensagem que diz o que o arquivo é.
        try { CompartilhamentoDeDespesas.Ler(backup); throw new Exception("Expected rejection"); } catch (ErroDeOrcamento ex) { Igual(true, ex.Message.Contains("backup")); }
        try { ArmazenamentoLocal.Ler(compartilhar); throw new Exception("Expected rejection"); } catch (ErroDeOrcamento ex) { Igual(true, ex.Message.Contains("compartilhamento")); }
        // Um armazenamento ativo também recusa o compartilhamento como base, sem alterar o que tem.
        using (var armazenamento = new ArmazenamentoLocal(Path.Combine(pasta, "base"))) { armazenamento.Salvar(remetente); var salvo = File.ReadAllText(armazenamento.CaminhoDoArquivo); Rejeitar(() => ArmazenamentoLocal.Ler(compartilhar)); Igual(salvo, File.ReadAllText(armazenamento.CaminhoDoArquivo)); }
        var valido = JsonNode.Parse(File.ReadAllText(compartilhar))!;
        void Adulterar(Action<JsonNode> mudanca) { var no = valido.DeepClone(); mudanca(no); File.WriteAllText(compartilhar, no.ToJsonString()); Rejeitar(() => CompartilhamentoDeDespesas.Ler(compartilhar)); }
        Adulterar(n => n["Kind"] = "backup"); Adulterar(n => n["App"] = "Outro"); Adulterar(n => n["FormatVersion"] = 2); Adulterar(n => n["FormatVersion"] = 0); Adulterar(n => n["Unknown"] = true);
        Adulterar(n => n["Expenses"] = new JsonArray()); Adulterar(n => n["Expenses"]![0]!["Participants"] = new JsonArray("Desconhecido")); Adulterar(n => n["Expenses"]![0]!["OriginId"] = Guid.Empty.ToString());
        Adulterar(n => n["Expenses"]![0]!["Extra"] = 1); Adulterar(n => n["People"] = new JsonArray("Ana", "ana")); Adulterar(n => n["Expenses"]![0]!["Category"] = "Inexistente");
        Adulterar(n => n["Expenses"]!.AsArray().Add(n["Expenses"]![0]!.DeepClone())); // identificador de origem repetido
        Adulterar(n => { var profundo = new JsonObject(); var cursor = profundo; for (var i = 0; i < 40; i++) { var interno = new JsonObject(); cursor["x"] = interno; cursor = interno; } n["People"] = profundo; });
        foreach (var bruto in new[] { "{", "null", "[]", "" }) { File.WriteAllText(compartilhar, bruto); Rejeitar(() => CompartilhamentoDeDespesas.Ler(compartilhar)); }
        using (var fluxo = File.OpenWrite(compartilhar)) fluxo.SetLength(6 * 1024 * 1024);
        Rejeitar(() => CompartilhamentoDeDespesas.Ler(compartilhar));
        // Quantidade acima do limite também é recusada.
        var muitas = CompartilhamentoDeDespesas.Montar(remetente, [remetente.Despesas[0].Id], false); for (var i = 0; i < CompartilhamentoDeDespesas.MaximoDeDespesas; i++) muitas.Despesas.Add(muitas.Despesas[0] with { IdDeOrigem = Guid.NewGuid() });
        Rejeitar(() => CompartilhamentoDeDespesas.Validar(muitas));
    }
    finally { Directory.Delete(pasta, true); }
});

// Tudo ou nada no disco: se a base resultante não é válida, nada é gravado e o arquivo atual e a cópia anterior ficam como estavam.
Teste("Import is atomic: a failure leaves memory and disk untouched", () =>
{
    var pasta = Path.Combine(Path.GetTempPath(), "bankmanager-test-" + Guid.NewGuid());
    try
    {
        var remetente = Remetente(); var recebedor = Recebedor();
        using var armazenamento = new ArmazenamentoLocal(pasta); armazenamento.Salvar(recebedor); var disco = File.ReadAllText(armazenamento.CaminhoDoArquivo); var memoria = Retrato(recebedor);
        var arquivo = CompartilhamentoDeDespesas.Montar(remetente, remetente.Despesas.Where(e => e.Titulo != "Segredo").Select(e => e.Id).ToList(), true);
        // A última despesa do arquivo fica sem responsável (Bruno ignorado, sem escolha de quem assume): as anteriores já tinham sido
        // montadas na cópia de trabalho, mas a falha descarta tudo.
        var quebrado = new MapeamentoDoCompartilhamento { Pessoas = new() { ["Ana"] = new(AcaoDaPessoa.Criar), ["Bruno"] = new(AcaoDaPessoa.Ignorar) } };
        Rejeitar(() => { var (proximo, _) = CompartilhamentoDeDespesas.Importar(recebedor, arquivo, quebrado); armazenamento.Salvar(proximo); });
        Igual(memoria, Retrato(recebedor)); Igual(disco, File.ReadAllText(armazenamento.CaminhoDoArquivo)); Igual(false, File.Exists(armazenamento.CaminhoDaCopia));
        // Com o mapeamento completo, salva de uma vez e a versão anterior vira a cópia automática.
        var (ok, resultado) = CompartilhamentoDeDespesas.Importar(recebedor, arquivo, CompartilhamentoDeDespesas.Sugerir(recebedor, arquivo)); armazenamento.Salvar(ok);
        Igual(6, resultado.Adicionadas.Count); Igual(disco, File.ReadAllText(armazenamento.CaminhoDaCopia)); Igual(7, armazenamento.Carregar().Despesas.Count); Igual(2500m, armazenamento.Carregar().Rendas.Single().Valor);
    }
    finally { Directory.Delete(pasta, true); }
});

// Executa toda a bateria, apresenta as falhas e produz um código de saída consumido pelo CI.
int falhas = 0;
foreach (var teste in testes)
{
    try { teste.Teste(); Console.WriteLine("PASS " + teste.Nome); }
    catch (Exception ex) { falhas++; Console.Error.WriteLine("FAIL " + teste.Nome + ": " + ex.Message); }
}
Console.WriteLine($"\n{testes.Count - falhas}/{testes.Count} tests passed.");
return falhas == 0 ? 0 : 1;
