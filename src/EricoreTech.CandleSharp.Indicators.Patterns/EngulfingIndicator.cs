using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Patterns
{
    // Candlestick pattern pack — an idea borrowed from Stock Indicators for
    // .NET's candlestick pattern indicators, mapped onto the uniform stance
    // contract: a pattern match IS a stance (Bullish/Bearish on the match bar,
    // Neutral otherwise), so pattern occurrences become engine triggers with no
    // special casing. Each indicator also emits a match column: +1 bullish
    // match, -1 bearish match, null otherwise.

    /// <summary>
    /// Engulfing: a body that fully engulfs the previous bar's opposite-colored
    /// body. Bullish after a down bar, bearish after an up bar.
    /// </summary>
    public sealed class EngulfingIndicator : ITechnicalIndicator
    {
        public string Name => "ENGULFING";

        public IndicatorResult Compute(IReadOnlyList<Candle> candles)
        {
            var match = new double?[candles.Count];
            var stance = new SignalDirection[candles.Count];
            for (int i = 1; i < candles.Count; i++)
            {
                var prev = candles[i - 1];
                var cur = candles[i];
                bool bullish = prev.Close < prev.Open && cur.Close > cur.Open
                    && cur.Open <= prev.Close && cur.Close >= prev.Open;
                bool bearish = prev.Close > prev.Open && cur.Close < cur.Open
                    && cur.Open >= prev.Close && cur.Close <= prev.Open;
                if (bullish) { match[i] = 1; stance[i] = SignalDirection.Bullish; }
                else if (bearish) { match[i] = -1; stance[i] = SignalDirection.Bearish; }
            }
            return new IndicatorResult([("ENGULFING", match)], stance);
        }
    }
}
