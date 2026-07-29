using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Patterns
{
    /// <summary>
    /// Marubozu: a bar that is nearly all body (no meaningful shadows) —
    /// conviction in the direction of the close.
    /// </summary>
    public sealed class MarubozuIndicator(double minBodyFraction = 0.95) : ITechnicalIndicator
    {
        public string Name => "MARUBOZU";

        public IndicatorResult Compute(IReadOnlyList<Candle> candles)
        {
            var match = new double?[candles.Count];
            var stance = new SignalDirection[candles.Count];
            for (int i = 0; i < candles.Count; i++)
            {
                var c = candles[i];
                double range = c.High - c.Low;
                if (range <= 0) continue;
                double body = Math.Abs(c.Close - c.Open);
                if (body / range < minBodyFraction || c.Close == c.Open) continue;

                bool up = c.Close > c.Open;
                match[i] = up ? 1 : -1;
                stance[i] = up ? SignalDirection.Bullish : SignalDirection.Bearish;
            }
            return new IndicatorResult([("MARUBOZU", match)], stance);
        }
    }
}
