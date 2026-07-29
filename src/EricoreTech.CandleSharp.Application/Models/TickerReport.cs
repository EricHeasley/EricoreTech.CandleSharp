using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>The full research packet for one ticker, assembled from stored data.</summary>
    public sealed record TickerReport(
        string Ticker,
        string Interval,
        Verdict Verdict,
        IReadOnlyList<AgentReport> Agents,
        IReadOnlyList<BacktestReport> Backtests,
        DividendSummary Dividends,
        SocialSentimentSummary Social,
        SimulationResult? Simulation);
}
