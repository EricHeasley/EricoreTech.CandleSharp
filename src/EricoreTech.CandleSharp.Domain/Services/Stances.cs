namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>Helpers for building stance arrays from common indicator shapes.</summary>
    public static class Stances
    {
        /// <summary>Bullish where a &gt; b, Bearish where a &lt; b. Crossovers, MACD-vs-signal, etc.</summary>
        public static SignalDirection[] FromComparison(double?[] a, double?[] b)
        {
            var stance = new SignalDirection[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] is not { } x || b[i] is not { } y) continue;
                stance[i] = x > y ? SignalDirection.Bullish
                    : x < y ? SignalDirection.Bearish
                    : SignalDirection.Neutral;
            }
            return stance;
        }

        /// <summary>Bullish below the low threshold, Bearish above the high one. RSI-style oscillators.</summary>
        public static SignalDirection[] FromThresholds(double?[] values, double bullishBelow, double bearishAbove)
        {
            var stance = new SignalDirection[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] is not { } v) continue;
                stance[i] = v < bullishBelow ? SignalDirection.Bullish
                    : v > bearishAbove ? SignalDirection.Bearish
                    : SignalDirection.Neutral;
            }
            return stance;
        }

        /// <summary>All-Neutral, for non-directional indicators (ATR, volume, ...).</summary>
        public static SignalDirection[] None(int count) => new SignalDirection[count];

        /// <summary>
        /// The current stance and the index where it began — "Bullish since bar 187".
        /// </summary>
        public static (SignalDirection Direction, int SinceIndex) LatestRun(IReadOnlyList<SignalDirection> stance)
        {
            if (stance.Count == 0) return (SignalDirection.Neutral, 0);
            var current = stance[^1];
            int since = stance.Count - 1;
            while (since > 0 && stance[since - 1] == current) since--;
            return (current, since);
        }
    }
}
