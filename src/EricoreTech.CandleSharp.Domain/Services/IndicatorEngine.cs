namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// Runs a set of indicators over a candle series and derives trigger points
    /// with one uniform rule: a trigger fires at bar i when an indicator's stance
    /// differs from the previous bar's and is directional (not Neutral). Dropping
    /// back to Neutral is not a trigger. No indicator gets bespoke trigger logic.
    /// </summary>
    public sealed class IndicatorEngine(IEnumerable<ITechnicalIndicator> indicators)
    {
        private readonly List<ITechnicalIndicator> _indicators = indicators.ToList();

        public EngineResult Run(IReadOnlyList<Candle> candles)
        {
            var columns = new List<(string Name, double?[] Values)>();
            var seenColumns = new HashSet<string>();
            var stances = new Dictionary<string, SignalDirection[]>();
            var triggers = new List<Trigger>();

            foreach (var indicator in _indicators)
            {
                IndicatorResult result;
                try
                {
                    result = indicator.Compute(candles);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Indicator {indicator.Name} failed: {ex.Message}", ex);
                }
                if (result.Stance.Length != candles.Count)
                    throw new InvalidOperationException(
                        $"{indicator.Name}: stance has {result.Stance.Length} entries for {candles.Count} candles");

                foreach (var column in result.Columns)
                {
                    if (column.Values.Length != candles.Count)
                        throw new InvalidOperationException(
                            $"{indicator.Name}: column {column.Name} misaligned with candles");
                    // Two indicators may share an input series (e.g. both use SMA_20);
                    // keep the first copy so the CSV has no duplicate columns.
                    if (seenColumns.Add(column.Name))
                        columns.Add(column);
                }

                stances[indicator.Name] = result.Stance;
                for (int i = 0; i < candles.Count; i++)
                {
                    var previous = i == 0 ? SignalDirection.Neutral : result.Stance[i - 1];
                    if (result.Stance[i] != previous && result.Stance[i] != SignalDirection.Neutral)
                        triggers.Add(new Trigger(candles[i].Timestamp, indicator.Name, result.Stance[i]));
                }
            }

            triggers.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            return new EngineResult(columns, triggers, stances);
        }
    }
}
