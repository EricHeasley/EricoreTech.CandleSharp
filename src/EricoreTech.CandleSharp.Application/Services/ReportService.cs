using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>
    /// Assembles the whole analysis pipeline into one report: verdict, every
    /// agent's view, the walk-forward backtest (the honesty check), dividend
    /// profile, crowd sentiment, and a one-year buy-and-hold simulation.
    /// </summary>
    public sealed class ReportService(
        AnalysisService analysis,
        MarketDataService market,
        SimulationService simulation,
        SocialService social)
    {
        public TickerReport Generate(
            string ticker,
            string interval = "1d",
            double simulationAmount = 10_000,
            BacktestOptions? backtestOptions = null)
        {
            var verdict = analysis.GetVerdict(ticker, interval);
            var agents = analysis.Analyze(ticker, interval);
            var backtests = analysis.Backtest(ticker, interval, backtestOptions ?? new BacktestOptions());
            var dividends = market.GetDividendSummary(ticker);
            var crowd = social.GetSummary(ticker);

            SimulationResult? sim = null;
            try
            {
                sim = simulation.BuyAndHold(ticker, simulationAmount);
            }
            catch (Exception ex) when (ex is ArgumentException or FileNotFoundException)
            {
                // Not enough daily history for a one-year window; the report notes it.
            }

            return new TickerReport(
                ticker.ToUpperInvariant(), interval, verdict, agents, backtests, dividends, crowd, sim);
        }
    }
}
