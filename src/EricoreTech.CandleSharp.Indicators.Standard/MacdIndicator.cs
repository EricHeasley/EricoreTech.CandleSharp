using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Standard
{
    /// <summary>Bullish while the MACD line is above its signal line.</summary>
    public sealed class MacdIndicator(int fast = 12, int slow = 26, int signal = 9) : ITechnicalIndicator
    {
        public string Name => $"MACD_{fast}_{slow}_{signal}";

        public IndicatorResult Compute(IReadOnlyList<Candle> candles)
        {
            var (macd, signalLine, histogram) = Indicators.Macd(candles, fast, slow, signal);
            return new IndicatorResult(
                [("MACD", macd), ("MACD_signal", signalLine), ("MACD_hist", histogram)],
                Stances.FromComparison(macd, signalLine));
        }
    }
}
