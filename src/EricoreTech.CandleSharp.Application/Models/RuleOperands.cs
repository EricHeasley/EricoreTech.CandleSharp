namespace EricoreTech.CandleSharp.Application
{
    /// <summary>
    /// What a rule can refer to for a dataset: numeric values (prices, volume,
    /// indicator columns) and indicator stances. Outputs that use future bars
    /// are left out.
    /// </summary>
    public sealed record RuleOperands(IReadOnlyList<string> Values, IReadOnlyList<string> Stances);
}
