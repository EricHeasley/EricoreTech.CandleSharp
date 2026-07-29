using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Advanced
{
    /// <summary>SuperTrend: stance follows the band direction directly.</summary>
    public sealed class SuperTrendIndicator(int window = 10, double multiplier = 3.0) : ITechnicalIndicator
    {
        public string Name => $"SUPERTREND_{window}";

        public IndicatorResult Compute(IReadOnlyList<Candle> candles)
        {
            var (line, direction) = Indicators.SuperTrend(candles, window, multiplier);
            var stance = new SignalDirection[candles.Count];
            // Skip the ATR warm-up window so the initial direction guess can't trigger.
            for (int i = window; i < candles.Count; i++)
                stance[i] = direction[i] == 1 ? SignalDirection.Bullish : SignalDirection.Bearish;
            return new IndicatorResult([("SUPERTREND", line)], stance);
        }
    }
}
