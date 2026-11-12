using System.Text;
using System.Text.Json;

namespace BankManager.Core;

/// <summary>
/// Arquivo de compartilhamento de despesas. É outro TIPO de arquivo, diferente do backup: leva só as despesas escolhidas
/// (com as pessoas citadas por nome e as categorias próprias que elas usam) e, ao ser importado, apenas ADICIONA despesas.
/// Nunca contém rendas, metas, acertos nem despesas não selecionadas.
/// </summary>
public sealed record ShareFile
{
    /// <summary>Valor do campo <see cref="Kind"/> que identifica um compartilhamento.</summary>
    public const string ShareKind = "share";
    /// <summary>Versão atual do formato do arquivo de compartilhamento (independente da versão do backup).</summary>
    public const int CurrentFormat = 1;
    /// <summary>Extensão própria, para o arquivo não ser confundido com um backup.</summary>
    public const string Extension = ".bmshare.json";
    /// <summary>Nome do aplicativo gravado no arquivo.</summary>
    public const string AppName = "BankManager";
    /// <summary>Tipo do arquivo; sempre "share".</summary>
    public string Kind { get; set; } = ShareKind;
    /// <summary>Aplicativo que gerou o arquivo.</summary>
    public string App { get; set; } = AppName;
    /// <summary>Versão do formato; um número maior que o conhecido pelo aplicativo é recusado.</summary>
    public int FormatVersion { get; set; } = CurrentFormat;
    /// <summary>Versão do aplicativo que gerou o arquivo (por exemplo "2.2"); só informativa, pode vir vazia.</summary>
    public string AppVersion { get; set; } = "";
    /// <summary>Nomes das pessoas citadas pelas despesas (responsáveis e, se incluído, quem pagou).</summary>
    public List<string> People { get; set; } = [];
    /// <summary>Categorias próprias usadas pelas despesas do arquivo.</summary>
    public List<string> CustomCategories { get; set; } = [];
    /// <summary>Despesas compartilhadas.</summary>
    public List<SharedExpense> Expenses { get; set; } = [];
}
/// <summary>
/// Uma despesa dentro do arquivo de compartilhamento. As pessoas aparecem pelo NOME, porque os identificadores de quem
/// envia não existem na base de quem recebe.
/// </summary>
public sealed record SharedExpense
{
    /// <summary>Identificador estável de origem: o mesmo em toda exportação da mesma despesa, usado para não duplicá-la.</summary>
    public Guid OriginId { get; set; }
    public string Title { get; set; } = "";
    public string Category { get; set; } = "Outros";
    public DateOnly DueDate { get; set; }
    public decimal Amount { get; set; }
    public List<ExpenseItem> Items { get; set; } = [];
    /// <summary>Grupo das parcelas na base de origem; na importação vira um grupo novo, igual para as parcelas do mesmo grupo.</summary>
    public Guid? InstallmentGroup { get; set; }
    public int InstallmentNumber { get; set; } = 1;
    public int InstallmentCount { get; set; } = 1;
    public SplitMode Split { get; set; }
    /// <summary>Nomes dos responsáveis.</summary>
    public List<string> Participants { get; set; } = [];
    /// <summary>Situação de pagamento; falsa quando quem exportou escolheu não incluí-la.</summary>
    public bool Paid { get; set; }
    public DateOnly? PaidDate { get; set; }
    /// <summary>Nome de quem pagou, se incluído.</summary>
    public string? PaidBy { get; set; }
}
/// <summary>
/// O que fazer com uma pessoa citada no arquivo.
/// </summary>
public enum PersonAction
{
    /// <summary>Usar uma pessoa já cadastrada.</summary>
    Existing,
    /// <summary>Criar um cadastro novo com o nome do arquivo.</summary>
    Create,
    /// <summary>Não trazer a pessoa: ela sai das despesas; as que ficarem sem responsável vão para a pessoa escolhida.</summary>
    Ignore
}
/// <summary>
/// Escolha para uma pessoa do arquivo; <paramref name="PersonId"/> só é usado em <see cref="PersonAction.Existing"/>.
/// </summary>
public sealed record PersonChoice(PersonAction Action, Guid? PersonId = null);
/// <summary>
/// Decisões de quem importa: como mapear pessoas e categorias, quem assume despesas sem responsável e as opções da importação.
/// </summary>
public sealed record ShareMapping
{
    /// <summary>Uma escolha para cada nome de <see cref="ShareFile.People"/>.</summary>
    public Dictionary<string, PersonChoice> People { get; init; } = [];
    /// <summary>Para cada categoria própria que não existe aqui: verdadeiro cria a categoria, falso usa "Outros".</summary>
    public Dictionary<string, bool> CreateCategories { get; init; } = [];
    /// <summary>Pessoa (já cadastrada) que assume as despesas que ficariam sem responsável; obrigatória só se houver alguma.</summary>
    public Guid? Fallback { get; init; }
    /// <summary>Importa as despesas proporcionais à renda como divisão igual.</summary>
    public bool EqualInsteadOfProportional { get; init; }
    /// <summary>Importa também TODAS as despesas que já existem aqui (como despesas novas, com outro Id).</summary>
    public bool ImportDuplicates { get; init; }
    /// <summary>Identificadores de origem das despesas já existentes que devem ser importadas mesmo assim, uma a uma.</summary>
    public IReadOnlySet<Guid> ImportAnyway { get; init; } = new HashSet<Guid>();
}
/// <summary>
/// O que acontece com uma despesa do arquivo na importação.
/// </summary>
public enum ShareLineStatus
{
    /// <summary>Será (ou foi) adicionada.</summary>
    Added,
    /// <summary>Já existe aqui e será pulada.</summary>
    Skipped,
    /// <summary>Tem um erro e não pode ser importada.</summary>
    Error
}
/// <summary>
/// Uma despesa do arquivo e o seu destino, na ordem do arquivo; alimenta a lista da prévia.
/// </summary>
/// <param name="Source">A despesa como veio no arquivo.</param>
/// <param name="Status">Se será adicionada, pulada ou se tem erro.</param>
/// <param name="Existing">Verdadeiro quando a despesa já existe aqui (mesmo que tenha sido importada mesmo assim).</param>
/// <param name="Expense">A despesa local criada, quando adicionada.</param>
/// <param name="Error">A explicação do erro, quando houver.</param>
public sealed record ShareLine(SharedExpense Source, ShareLineStatus Status, bool Existing, Expense? Expense = null, string? Error = null);
/// <summary>
/// Resultado de uma importação (ou da sua prévia).
/// </summary>
/// <param name="Added">Despesas adicionadas, já com Ids locais.</param>
/// <param name="Duplicates">Despesas puladas por já existirem aqui (mesmo identificador de origem).</param>
/// <param name="Errors">Uma mensagem por despesa do arquivo que não pôde ser importada.</param>
/// <param name="NewPeople">Nomes das pessoas cadastradas pela importação.</param>
/// <param name="NewCategories">Categorias próprias criadas pela importação.</param>
/// <param name="Proportional">Quantas despesas adicionadas usam a divisão proporcional à renda (recalculada com as rendas locais).</param>
/// <param name="Lines">Uma linha por despesa do arquivo, na ordem do arquivo, com o destino de cada uma.</param>
public sealed record ShareResult(List<Expense> Added, int Duplicates, List<string> Errors, List<string> NewPeople, List<string> NewCategories, int Proportional, List<ShareLine> Lines);
/// <summary>
/// Exporta despesas escolhidas para um arquivo de compartilhamento e importa esse arquivo ADICIONANDO despesas, sem substituir
/// nem remover nada. Trabalha sobre os dados recebidos; só <see cref="Save"/> e <see cref="Read"/> tocam o disco.
/// </summary>
public static class ExpenseShare
{
    /// <summary>Máximo de despesas em um arquivo de compartilhamento.</summary>
    public const int MaxExpenses = 5_000;
    /// <summary>Tamanho máximo do arquivo de compartilhamento.</summary>
    public const long MaxBytes = 5 * 1024 * 1024;
    /// <summary>
    /// Monta o conteúdo do arquivo com as despesas indicadas. Sem <paramref name="includePayment"/>, todas saem "em aberto",
    /// sem data de pagamento e sem quem pagou. O vínculo com recorrências não é levado: cada despesa vai como despesa comum.
    /// </summary>
    /// <param name="appVersion">Versão do aplicativo, gravada só para informar quem recebe (por exemplo "2.2").</param>
    public static ShareFile Build(BudgetData data, IReadOnlyCollection<Guid> ids, bool includePayment, string appVersion = "")
    {
        if (ids.Count == 0) throw new BudgetException("Selecione pelo menos uma despesa para compartilhar.");
        if (ids.Count > MaxExpenses) throw new BudgetException($"Compartilhe até {MaxExpenses} despesas por arquivo.");
        var wanted = ids.ToHashSet(); var chosen = data.Expenses.Where(e => wanted.Contains(e.Id)).OrderBy(e => e.DueDate).ThenBy(e => e.Title).ToList();
        if (chosen.Count != wanted.Count) throw new BudgetException("Uma das despesas selecionadas não existe mais. Atualize a lista e tente de novo.");
        var names = data.People.ToDictionary(p => p.Id, p => p.Name);
        var file = new ShareFile { AppVersion = appVersion };
        foreach (var e in chosen)
        {
            var paid = includePayment && e.Paid;
            file.Expenses.Add(new SharedExpense
            {
                // Uma despesa que já veio de outro arquivo mantém a origem inicial, para ser reconhecida por toda a cadeia.
                OriginId = e.OriginId ?? e.Id, Title = e.Title, Category = e.Category, DueDate = e.DueDate, Amount = e.Amount, Items = e.Items.Select(i => i with { }).ToList(),
                InstallmentGroup = e.InstallmentGroup, InstallmentNumber = e.InstallmentNumber, InstallmentCount = e.InstallmentCount, Split = e.Split,
                Participants = e.Participants.Select(id => names[id]).ToList(), Paid = paid, PaidDate = paid ? e.PaidDate : null, PaidBy = paid && e.PaidBy is { } payer ? names[payer] : null
            });
        }
        // Só as pessoas e as categorias próprias realmente citadas vão para o arquivo.
        var cited = file.Expenses.SelectMany(e => e.PaidBy is null ? e.Participants : e.Participants.Append(e.PaidBy)).ToHashSet();
        file.People = data.People.Select(p => p.Name).Where(cited.Contains).ToList();
        file.CustomCategories = data.CustomCategories.Where(c => file.Expenses.Any(e => e.Category == c)).ToList();
        Validate(file); return file;
    }
    /// <summary>
    /// Confere a estrutura do arquivo: tipo, aplicativo, versão do formato, quantidades e referências. Os valores de cada despesa
    /// (descrição, valor, datas) são conferidos na importação, que aponta o erro por despesa.
    /// </summary>
    public static void Validate(ShareFile file)
    {
        if (file.Kind != ShareFile.ShareKind || file.App != ShareFile.AppName) throw new BudgetException("Este arquivo não é um compartilhamento de despesas do BankManager.");
        if (file.FormatVersion > ShareFile.CurrentFormat) throw new BudgetException("Este arquivo foi criado por uma versão mais nova do BankManager. Atualize o aplicativo para importá-lo.");
        if (file.FormatVersion < 1) throw new BudgetException("Versão do arquivo de compartilhamento inválida.");
        if (file.AppVersion is null || file.AppVersion.Length > 20 || file.AppVersion.Any(c => !char.IsAsciiDigit(c) && c != '.')) throw new BudgetException("O arquivo de compartilhamento traz uma versão do aplicativo inválida.");
        if (file.People is null || file.CustomCategories is null || file.Expenses is null || file.People.Count > 200 || file.CustomCategories.Count > CategoryEngine.MaxCustom || file.Expenses.Count > MaxExpenses)
            throw new BudgetException("Arquivo de compartilhamento inválido ou com registros demais.");
        if (file.Expenses.Count == 0) throw new BudgetException("O arquivo de compartilhamento não tem nenhuma despesa.");
        var people = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in file.People) { BudgetRules.Text(name, "Nome", 80); if (name != name.Trim() || !people.Add(name)) throw new BudgetException("O arquivo traz um nome de pessoa repetido ou inválido."); }
        var categories = new HashSet<string>(BudgetRules.Categories, StringComparer.OrdinalIgnoreCase);
        foreach (var name in file.CustomCategories) { BudgetRules.Text(name, "Categoria", CategoryEngine.MaxLength); if (name != name.Trim() || !categories.Add(name)) throw new BudgetException("O arquivo traz uma categoria repetida ou inválida."); }
        var origins = new HashSet<Guid>();
        foreach (var e in file.Expenses)
        {
            if (e is null || e.OriginId == Guid.Empty || !origins.Add(e.OriginId)) throw new BudgetException("O arquivo traz uma despesa sem identificador de origem ou com identificador repetido.");
            if (e.Participants is null || e.Participants.Count > 200 || e.Participants.Any(n => n is null || !people.Contains(n)) || (e.PaidBy is not null && !people.Contains(e.PaidBy)))
                throw new BudgetException("O arquivo cita uma pessoa que não está na sua lista de pessoas.");
            if (e.Items is null || e.Items.Count > 500 || e.Items.Any(i => i is null)) throw new BudgetException("O arquivo traz uma lista de itens inválida.");
            if (e.Category is null || !categories.Contains(e.Category)) throw new BudgetException("O arquivo usa uma categoria que não está na sua lista de categorias.");
        }
    }
    /// <summary>
    /// Valida e grava o arquivo de compartilhamento de forma atômica (temporário + troca), sem tocar na base do aplicativo.
    /// </summary>
    public static void Save(string path, ShareFile file)
    {
        Validate(file); var json = JsonSerializer.Serialize(file, LocalStore.JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new BudgetException("O arquivo de compartilhamento ficaria grande demais. Selecione menos despesas.");
        LocalStore.AtomicWrite(path, json);
    }
    /// <summary>
    /// Lê e valida um arquivo de compartilhamento com as mesmas proteções do backup: limite de tamanho, profundidade limitada e
    /// recusa de campos desconhecidos. Um backup escolhido por engano é recusado com uma mensagem própria.
    /// </summary>
    public static ShareFile Read(string path)
    {
        ShareFile file;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length > MaxBytes) throw new BudgetException("O arquivo de compartilhamento deve ter no máximo 5 MB.");
            try { file = JsonSerializer.Deserialize<ShareFile>(stream, LocalStore.JsonOptions) ?? throw new BudgetException("Arquivo vazio."); }
            catch (JsonException)
            {
                stream.Dispose();
                if (LooksLikeBackup(path)) throw new BudgetException("Este arquivo é um backup completo, não um compartilhamento de despesas. Para restaurar um backup, use “Importar backup” em Ajustes. Nada foi alterado.");
                throw new BudgetException("O arquivo não é um compartilhamento de despesas válido do BankManager. Nada foi alterado.");
            }
        }
        Validate(file); return file;
    }
    /// <summary>Diz se o arquivo tem cara de compartilhamento (campo Kind igual a "share"), para uma mensagem de erro mais clara.</summary>
    internal static bool LooksLikeShare(string path) => Peek(path, root => root.TryGetProperty("Kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == ShareFile.ShareKind);
    /// <summary>Diz se o arquivo tem cara de backup (tem Version e não tem Kind).</summary>
    internal static bool LooksLikeBackup(string path) => Peek(path, root => root.TryGetProperty("Version", out _) && !root.TryGetProperty("Kind", out _));
    // Abre o JSON só para olhar os campos do topo; qualquer falha significa "não reconhecido".
    private static bool Peek(string path, Func<JsonElement, bool> test)
    {
        try
        {
            if (new FileInfo(path).Length > 20 * 1024 * 1024) return false;
            using var document = JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions { MaxDepth = 32 });
            return document.RootElement.ValueKind == JsonValueKind.Object && test(document.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return false; }
    }
    /// <summary>
    /// Categorias próprias do arquivo que ainda não existem na base (comparando sem diferenciar maiúsculas).
    /// </summary>
    public static List<string> MissingCategories(BudgetData data, ShareFile file) =>
        file.CustomCategories.Where(c => !BudgetRules.AllCategories(data).Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
    /// <summary>
    /// Sugere um mapeamento: cada nome vira a pessoa cadastrada com o mesmo nome (ignorando maiúsculas e acentos) ou um cadastro
    /// novo; categorias que faltam são criadas enquanto couberem no limite. É só uma sugestão: a tela sempre a mostra para confirmação.
    /// </summary>
    public static ShareMapping Suggest(BudgetData data, ShareFile file)
    {
        var people = new Dictionary<string, PersonChoice>();
        foreach (var name in file.People)
        {
            var match = data.People.FirstOrDefault(p => ExpenseSearch.Fold(p.Name) == ExpenseSearch.Fold(name));
            people[name] = match is null ? new PersonChoice(PersonAction.Create) : new PersonChoice(PersonAction.Existing, match.Id);
        }
        var room = CategoryEngine.MaxCustom - data.CustomCategories.Count; var categories = new Dictionary<string, bool>();
        foreach (var name in MissingCategories(data, file)) categories[name] = room-- > 0;
        return new ShareMapping { People = people, CreateCategories = categories };
    }
    /// <summary>
    /// Calcula a importação sem alterar <paramref name="data"/>: trabalha em uma cópia profunda, adiciona as despesas e valida o
    /// orçamento inteiro. Serve de prévia e também de base para o salvamento (tudo ou nada).
    /// </summary>
    /// <returns>A nova base (com tudo o que já existia, intacto, mais as despesas adicionadas) e o resumo do que foi feito.</returns>
    public static (BudgetData Next, ShareResult Result) Import(BudgetData data, ShareFile file, ShareMapping mapping)
    {
        var next = LocalStore.Clone(data); var result = Apply(next, file, mapping); BudgetRules.Validate(next); return (next, result);
    }
    /// <summary>
    /// Adiciona as despesas do arquivo a <paramref name="data"/> (que deve ser uma cópia de trabalho). Não remove nem altera rendas,
    /// metas, acertos ou despesas existentes. Uma exceção significa que nada deve ser aproveitado.
    /// </summary>
    public static ShareResult Apply(BudgetData data, ShareFile file, ShareMapping mapping)
    {
        Validate(file);
        var existing = data.People.ToDictionary(p => p.Id);
        var createdPeople = new Dictionary<string, Person>(StringComparer.OrdinalIgnoreCase); var createdCategories = new List<string>();
        // Pessoa do arquivo → Id local (nulo quando ignorada). Cadastros novos nascem só quando a primeira despesa precisa deles.
        Guid? Resolve(string name)
        {
            if (!mapping.People.TryGetValue(name, out var choice)) throw new BudgetException($"Escolha o que fazer com “{name}”: usar uma pessoa cadastrada, criar um cadastro ou ignorar.");
            switch (choice.Action)
            {
                case PersonAction.Ignore: return null;
                case PersonAction.Existing:
                    if (choice.PersonId is not { } id || !existing.ContainsKey(id)) throw new BudgetException($"A pessoa escolhida para “{name}” não existe mais.");
                    return id;
                default:
                    // Se já há alguém com esse nome exato, usa o cadastro existente em vez de tentar criar um nome repetido.
                    var same = data.People.FirstOrDefault(p => string.Equals(p.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
                    if (same is not null) return same.Id;
                    var person = new Person { Name = name }; data.People.Add(person); createdPeople[name] = person; return person.Id;
            }
        }
        // Categoria do arquivo → categoria local: padrão, própria já existente, criada agora ou "Outros".
        string Category(string name)
        {
            var known = BudgetRules.AllCategories(data).FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
            if (known is not null) return known;
            if (!mapping.CreateCategories.GetValueOrDefault(name)) return "Outros";
            if (data.CustomCategories.Count >= CategoryEngine.MaxCustom) throw new BudgetException($"O limite de {CategoryEngine.MaxCustom} categorias próprias seria ultrapassado. Escolha “Outros” para algumas categorias do arquivo.");
            data.CustomCategories.Add(name); createdCategories.Add(name); return name;
        }
        // Já existe aqui: mesmo Id (a despesa nasceu nesta base) ou mesma origem (já foi importada antes).
        var known = data.Expenses.Select(e => e.Id).Concat(data.Expenses.Where(e => e.OriginId is not null).Select(e => e.OriginId!.Value)).ToHashSet();
        var groups = new Dictionary<Guid, Guid>(); var added = new List<Expense>(); var errors = new List<string>(); var lines = new List<ShareLine>(); var duplicates = 0; var proportional = 0;
        for (var index = 0; index < file.Expenses.Count; index++)
        {
            var shared = file.Expenses[index];
            var exists = known.Contains(shared.OriginId);
            if (exists && !mapping.ImportDuplicates && !mapping.ImportAnyway.Contains(shared.OriginId)) { duplicates++; lines.Add(new ShareLine(shared, ShareLineStatus.Skipped, true)); continue; }
            var resolved = shared.Participants.Select(name => (Name: name, Id: Resolve(name))).Where(p => p.Id is not null).ToList();
            // Duas pessoas do arquivo ligadas à mesma pessoa daqui mudariam a divisão sem avisar: quem importa precisa escolher de novo.
            var merged = resolved.GroupBy(p => p.Id).FirstOrDefault(g => g.Count() > 1);
            if (merged is not null) throw new BudgetException($"“{merged.First().Name}” e “{merged.Last().Name}” foram ligados à mesma pessoa, mas dividem a despesa “{shared.Title}”. Escolha pessoas diferentes ou ignore uma delas.");
            var participants = resolved.Select(p => p.Id!.Value).ToList();
            if (participants.Count == 0)
            {
                // Todos os responsáveis foram ignorados: a despesa precisa de alguém, e essa escolha é de quem importa.
                if (mapping.Fallback is not { } fallback || !existing.ContainsKey(fallback)) throw new BudgetException("Algumas despesas ficariam sem responsável. Escolha quem assume essas despesas.");
                participants = [fallback];
            }
            var split = shared.Split == SplitMode.AvailableIncome && mapping.EqualInsteadOfProportional ? SplitMode.Equal : shared.Split;
            // Uma despesa individual cujo responsável mudou continua individual, agora com a pessoa que restou.
            if (split == SplitMode.Individual && participants.Count > 1) participants = [participants[0]];
            Guid? group = shared.InstallmentGroup is { } old ? groups.TryGetValue(old, out var mapped) ? mapped : groups[old] = Guid.NewGuid() : null;
            var expense = new Expense
            {
                Title = shared.Title?.Trim() ?? "", Category = Category(shared.Category), DueDate = shared.DueDate, Amount = shared.Amount, Items = shared.Items.Select(i => i with { }).ToList(),
                InstallmentGroup = group, InstallmentNumber = shared.InstallmentNumber, InstallmentCount = shared.InstallmentCount, Split = split, Participants = participants,
                Paid = shared.Paid, PaidDate = shared.Paid ? shared.PaidDate : null, PaidBy = shared.Paid && shared.PaidBy is not null ? Resolve(shared.PaidBy) : null, OriginId = shared.OriginId
            };
            // Confere a despesa sozinha, com as pessoas e categorias locais: um erro vira uma linha na prévia e as outras seguem.
            try { BudgetRules.Validate(new BudgetData { People = data.People, CustomCategories = data.CustomCategories, Expenses = [expense] }); }
            catch (BudgetException ex) { errors.Add($"Despesa {index + 1} ({(string.IsNullOrWhiteSpace(shared.Title) ? "sem descrição" : shared.Title.Trim())}): {ex.Message}"); lines.Add(new ShareLine(shared, ShareLineStatus.Error, exists, Error: ex.Message)); continue; }
            data.Expenses.Add(expense); added.Add(expense); lines.Add(new ShareLine(shared, ShareLineStatus.Added, exists, expense)); if (split == SplitMode.AvailableIncome) proportional++;
        }
        // Cadastros e categorias criados para despesas que acabaram não entrando são desfeitos.
        var usedPeople = added.SelectMany(e => e.PaidBy is { } p ? e.Participants.Append(p) : e.Participants).ToHashSet();
        foreach (var (name, person) in createdPeople.Where(c => !usedPeople.Contains(c.Value.Id)).ToList()) { data.People.Remove(person); createdPeople.Remove(name); }
        foreach (var name in createdCategories.Where(c => added.All(e => e.Category != c)).ToList()) { data.CustomCategories.Remove(name); createdCategories.Remove(name); }
        return new ShareResult(added, duplicates, errors, createdPeople.Values.Select(p => p.Name).ToList(), createdCategories, proportional, lines);
    }
}
