namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>Total dividends paid in one calendar year.</summary>
    public sealed record AnnualDividend(int Year, double Total);
}
