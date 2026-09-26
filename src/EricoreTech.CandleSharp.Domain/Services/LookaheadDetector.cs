namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// Finds indicator outputs that peek at the future — values for bar i that
    /// depend on bars after i (for example Ichimoku's Chikou span, which is the
    /// close 26 bars LATER plotted back). Harmless on a chart, fatal in pattern
    /// analysis, where they "predict" returns perfectly. Detection is empirical,
    /// so it covers plugins too: run the engine on full and on truncated history
    /// and flag every column or stance whose already-known bars change once
    /// later bars exist.
    /// </summary>
    public static class LookaheadDetector
    {
        public static (IReadOnlySet<string> Columns, IReadOnlySet<string> Stances) Find(
            IndicatorEngine engine, IReadOnlyList<Candle> candles, int probe = 60)
        {
            var columns = new HashSet<string>();
            var stances = new HashSet<string>();
            probe = Math.Min(probe, candles.Count / 4);
            if (probe < 1) return (columns, stances);

            int cut = candles.Count - probe;
            var full = engine.Run(candles);
            var truncated = engine.Run(candles.Take(cut).ToList());

            foreach (var (name, values) in truncated.Columns)
            {
                var fullValues = full.Columns.FirstOrDefault(c => c.Name == name).Values;
                if (fullValues is null) continue;
                for (int i = cut - probe; i < cut; i++)
                    if (!Same(values[i], fullValues[i]))
                    {
                        columns.Add(name);
                        break;
                    }
            }
            foreach (var (name, stance) in truncated.Stances)
            {
                if (!full.Stances.TryGetValue(name, out var fullStance)) continue;
                for (int i = cut - probe; i < cut; i++)
                    if (stance[i] != fullStance[i])
                    {
                        stances.Add(name);
                        break;
                    }
            }
            return (columns, stances);
        }

        private static bool Same(double? a, double? b) =>
            a is null ? b is null
            : b is not null && Math.Abs(a.Value - b.Value) <= 1e-9 * Math.Max(1, Math.Abs(a.Value));
    }
}
