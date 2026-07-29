using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Advanced
{
    /// <summary>
    /// ADX as a trend filter: Neutral while ADX is below the strength threshold
    /// (no trend worth following), otherwise directional by +DI vs -DI. A good
    /// example of a stance that combines two conditions.
    /// </summary>
    public sealed class AdxIndicator(int window = 14, double trendThreshold = 25) : ITechnicalIndicator
    {
        public string Name => $"ADX_{window}";

        public IndicatorResult Compute(IReadOnlyList<Candle> candles)
        {
            var (adx, plusDi, minusDi) = Indicators.Adx(candles, window);
            var stance = new SignalDirection[candles.Count];
            for (int i = 0; i < candles.Count; i++)
            {
                if (adx[i] is not { } strength || strength < trendThreshold) continue;
                if (plusDi[i] is not { } p || minusDi[i] is not { } m || p == m) continue;
                stance[i] = p > m ? SignalDirection.Bullish : SignalDirection.Bearish;
            }
            return new IndicatorResult(
                [($"ADX_{window}", adx), ("DI_plus", plusDi), ("DI_minus", minusDi)],
                stance);
        }
    }
}
