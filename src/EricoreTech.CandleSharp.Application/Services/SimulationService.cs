using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>Buy-and-hold simulation over stored daily data and dividend history.</summary>
    public sealed class SimulationService(ICandleRepository repository, IDividendRepository dividendRepository)
    {
        /// <summary>Defaults to a one-year window ending at the latest stored daily bar.</summary>
        public SimulationResult BuyAndHold(string ticker, double amount, DateTime? start = null, DateTime? end = null)
        {
            var candles = repository.Load(ticker, "1d");
            var dividends = dividendRepository.LoadDividends(ticker);
            var endDate = end ?? candles[^1].Timestamp;
            var startDate = start ?? endDate.AddYears(-1);
            return Simulator.BuyAndHold(ticker.ToUpperInvariant(), candles, dividends, amount, startDate, endDate);
        }
    }
}
