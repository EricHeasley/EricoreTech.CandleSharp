namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>Dividend profile of a ticker: current yield plus growth history.</summary>
    public sealed record DividendSummary(
        string Ticker,
        int PaymentCount,
        DateTime? LastPaymentDate,
        double? LastAmount,
        double TrailingYearTotal,
        double? YieldPercent,
        double? GrowthYoYPercent,
        double? Growth3YPercent,
        double? Growth5YPercent,
        int ConsecutiveGrowthYears,
        IReadOnlyList<AnnualDividend> AnnualTotals,
        IReadOnlyList<Dividend> RecentPayments);
}
