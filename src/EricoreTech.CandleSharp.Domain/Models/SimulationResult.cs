namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>Outcome of a buy-and-hold simulation with dividends taken as cash.</summary>
    public sealed record SimulationResult(
        string Ticker,
        DateTime BuyDate,
        DateTime SellDate,
        double Invested,
        double Shares,
        double BuyPrice,
        double SellPrice,
        double EndStockValue,
        double DividendCash,
        int DividendPayments,
        double EndTotalValue,
        double TotalReturnPercent,
        double PriceReturnPercent,
        double DividendYieldOnCostPercent,
        double ReinvestedValue,
        double ReinvestedReturnPercent);
}
