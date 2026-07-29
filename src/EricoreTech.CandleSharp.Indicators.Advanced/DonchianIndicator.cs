using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Advanced
{
    /// <summary>
    /// Donchian breakout: Bullish when the close breaks the PRIOR bar's upper
    /// channel (a new N-bar high), Bearish on a break of the lower. Momentum
    /// semantics — the opposite sign convention from the mean-reversion band
    /// stance used by Bollinger.
    /// </summary>
    public sealed class DonchianIndicator(int window = 20) : ITechnicalIndicator
    {
        public string Name => $"DONCHIAN_{window}";

        public IndicatorResult Compute(IReadOnlyList<Candle> candles)
        {
            var (upper, mid, lower) = Indicators.Donchian(candles, window);
            var stance = new SignalDirection[candles.Count];
            for (int i = 1; i < candles.Count; i++)
            {
                if (upper[i - 1] is not { } prevUpper || lower[i - 1] is not { } prevLower) continue;
                double close = candles[i].Close;
                if (close > prevUpper) stance[i] = SignalDirection.Bullish;
                else if (close < prevLower) stance[i] = SignalDirection.Bearish;
            }
            return new IndicatorResult(
                [($"DONCHIAN_upper_{window}", upper), ($"DONCHIAN_mid_{window}", mid),
                 ($"DONCHIAN_lower_{window}", lower)],
                stance);
        }
    }
}
