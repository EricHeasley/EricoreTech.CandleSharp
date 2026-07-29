using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Standard
{
    /// <summary>Mean-reversion stance: Bullish on a close below the lower band, Bearish above the upper.</summary>
    public sealed class BollingerBandsIndicator(int window = 20, double numStd = 2.0) : ITechnicalIndicator
    {
        public string Name => $"BB_{window}";

        public IndicatorResult Compute(IReadOnlyList<Candle> candles)
        {
            var (mid, upper, lower) = Indicators.BollingerBands(candles, window, numStd);
            var stance = new SignalDirection[candles.Count];
            for (int i = 0; i < candles.Count; i++)
            {
                if (upper[i] is not { } up || lower[i] is not { } lo) continue;
                double close = candles[i].Close;
                stance[i] = close < lo ? SignalDirection.Bullish
                    : close > up ? SignalDirection.Bearish
                    : SignalDirection.Neutral;
            }
            return new IndicatorResult(
                [($"BB_mid_{window}", mid), ($"BB_upper_{window}", upper), ($"BB_lower_{window}", lower)],
                stance);
        }
    }
}
