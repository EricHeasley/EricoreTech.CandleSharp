using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Patterns
{
    /// <summary>
    /// Hammer / shooting star: a small body with one long shadow. A long lower
    /// shadow (hammer) is the bullish rejection shape; a long upper shadow
    /// (shooting star) the bearish one.
    /// </summary>
    public sealed class HammerIndicator(double shadowToBody = 2.0, double maxOppositeShadow = 0.25)
        : ITechnicalIndicator
    {
        public string Name => "HAMMER";

        public IndicatorResult Compute(IReadOnlyList<Candle> candles)
        {
            var match = new double?[candles.Count];
            var stance = new SignalDirection[candles.Count];
            for (int i = 0; i < candles.Count; i++)
            {
                var c = candles[i];
                double range = c.High - c.Low;
                double body = Math.Abs(c.Close - c.Open);
                if (range <= 0 || body <= 0) continue;
                double upper = c.High - Math.Max(c.Open, c.Close);
                double lower = Math.Min(c.Open, c.Close) - c.Low;

                if (lower >= shadowToBody * body && upper <= maxOppositeShadow * range)
                {
                    match[i] = 1;
                    stance[i] = SignalDirection.Bullish;   // hammer
                }
                else if (upper >= shadowToBody * body && lower <= maxOppositeShadow * range)
                {
                    match[i] = -1;
                    stance[i] = SignalDirection.Bearish;   // shooting star
                }
            }
            return new IndicatorResult([("HAMMER", match)], stance);
        }
    }
}
