namespace BankManager.Nucleo;

/// <summary>
/// Situação de uma despesa em relação ao seu vencimento. Serve de base para os chips da interface.
/// </summary>
public enum TipoDePrazo
{
    /// <summary>A despesa já foi quitada.</summary>
    Paga,
    /// <summary>Em aberto, com vencimento ainda distante.</summary>
    EmAberto,
    /// <summary>Em aberto e vence hoje ou nos próximos dias (veja <see cref="SituacaoDoPrazo.DiasDeAviso"/>).</summary>
    VenceEmBreve,
    /// <summary>Em aberto e já passou do vencimento.</summary>
    Atrasada
}
/// <summary>
/// Resultado de <see cref="De"/>: a categoria da situação e quantos dias faltam (negativo se atrasada).
/// </summary>
/// <param name="Tipo">Categoria da situação.</param>
/// <param name="Dias">Dias até o vencimento: 0 é hoje, negativo é atraso. Sem sentido para despesas pagas.</param>
public readonly record struct SituacaoDoPrazo(TipoDePrazo Tipo, int Dias)
{
    /// <summary>Até quantos dias antes do vencimento a despesa passa a ser destacada como "vence em breve".</summary>
    public const int DiasDeAviso = 3;
    /// <summary>
    /// Calcula a situação de uma despesa. A data de referência é recebida como parâmetro para que
    /// o cálculo seja determinístico nos testes (a interface passa o dia de hoje).
    /// </summary>
    public static SituacaoDoPrazo De(Despesa despesa, DateOnly hoje)
    {
        var dias = despesa.Vencimento.DayNumber - hoje.DayNumber;
        if (despesa.Paga) return new(TipoDePrazo.Paga, dias);
        return new(dias < 0 ? TipoDePrazo.Atrasada : dias <= DiasDeAviso ? TipoDePrazo.VenceEmBreve : TipoDePrazo.EmAberto, dias);
    }
}
