namespace BankManager.Nucleo;

/// <summary>
/// Gerencia as categorias criadas pelo usuário. As categorias padrão (<see cref="RegrasDoOrcamento.Categorias"/>) são fixas e
/// nunca são alteradas aqui. Trabalha sobre os dados recebidos e não acessa disco.
/// </summary>
public static class MotorDeCategorias
{
    /// <summary>Quantidade máxima de categorias próprias.</summary>
    public const int MaximoDeProprias = 40;
    /// <summary>Tamanho máximo do nome de uma categoria.</summary>
    public const int TamanhoMaximo = 30;
    /// <summary>
    /// Cria uma categoria. Rejeita nome vazio, longo, com caracteres de controle ou já existente (ignorando maiúsculas e minúsculas).
    /// </summary>
    public static void Adicionar(DadosDoOrcamento dados, string nome)
    {
        nome = Normalizar(nome);
        if (Existe(dados, nome)) throw new ErroDeOrcamento("Já existe uma categoria com esse nome.");
        if (dados.CategoriasProprias.Count >= MaximoDeProprias) throw new ErroDeOrcamento($"Você já tem {MaximoDeProprias} categorias próprias. Exclua alguma antes de criar outra.");
        dados.CategoriasProprias.Add(nome);
    }
    /// <summary>
    /// Renomeia uma categoria própria e atualiza, de uma vez, todas as despesas e recorrências que a usam.
    /// Trocar só maiúsculas e minúsculas (por exemplo "pets" para "Pets") é permitido.
    /// </summary>
    public static void Renomear(DadosDoOrcamento dados, string nomeAntigo, string nomeNovo)
    {
        var indice = dados.CategoriasProprias.IndexOf(nomeAntigo);
        if (indice < 0) throw new ErroDeOrcamento("As categorias padrão não podem ser renomeadas.");
        nomeNovo = Normalizar(nomeNovo);
        if (!string.Equals(nomeAntigo, nomeNovo, StringComparison.OrdinalIgnoreCase) && Existe(dados, nomeNovo)) throw new ErroDeOrcamento("Já existe uma categoria com esse nome.");
        dados.CategoriasProprias[indice] = nomeNovo;
        foreach (var despesa in dados.Despesas.Where(e => e.Categoria == nomeAntigo)) despesa.Categoria = nomeNovo;
        foreach (var recorrencia in dados.Recorrencias.Where(r => r.Categoria == nomeAntigo)) recorrencia.Categoria = nomeNovo;
        foreach (var meta in dados.Metas.Where(g => g.Categoria == nomeAntigo)) meta.Categoria = nomeNovo;
    }
    /// <summary>
    /// Exclui uma categoria própria, desde que nenhuma despesa nem recorrência a use.
    /// </summary>
    public static void Remover(DadosDoOrcamento dados, string nome)
    {
        if (!dados.CategoriasProprias.Contains(nome)) throw new ErroDeOrcamento("As categorias padrão não podem ser excluídas.");
        var despesas = dados.Despesas.Count(e => e.Categoria == nome); var recorrencias = dados.Recorrencias.Count(r => r.Categoria == nome);
        if (despesas + recorrencias > 0) throw new ErroDeOrcamento($"Esta categoria é usada por {despesas} despesa(s) e {recorrencias} recorrência(s). Mude a categoria delas antes de excluí-la.");
        // A meta de uma categoria excluída perde o sentido e sai junto.
        dados.CategoriasProprias.Remove(nome); dados.Metas.RemoveAll(g => g.Categoria == nome);
    }
    private static string Normalizar(string? nome)
    {
        nome = nome?.Trim() ?? ""; RegrasDoOrcamento.Texto(nome, "Nome da categoria", TamanhoMaximo); return nome;
    }
    private static bool Existe(DadosDoOrcamento dados, string nome) => RegrasDoOrcamento.TodasAsCategorias(dados).Any(c => string.Equals(c, nome, StringComparison.OrdinalIgnoreCase));
}
