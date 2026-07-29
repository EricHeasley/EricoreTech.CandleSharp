using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Standard
{
    /// <summary>Mean-reversion stance: Bullish when oversold, Bearish when overbought.</summary>
    public sealed class RsiIndicator(int window = 14, double oversold = 30, double overbought = 70)
        : ITechnicalIndicator
    {
        public string Name => $"RSI_{window}";

        public IndicatorResult Compute(IReadOnlyList<Candle> candles)
        {
            var rsi = Indicators.Rsi(candles, window);
            return new IndicatorResult(
                [($"RSI_{window}", rsi)],
                Stances.FromThresholds(rsi, bullishBelow: oversold, bearishAbove: overbought));
        }
    }
}
