namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// A stored indicator history for one ticker: per bar, the close plus every
    /// indicator column value and every indicator's stance. Column-oriented and
    /// bar-aligned like <see cref="EngineResult"/>, so it can be persisted as a
    /// journal and analyzed against the price moves that followed.
    /// </summary>
    public sealed record SnapshotTable(
        string Ticker,
        IReadOnlyList<DateTime> Timestamps,
        IReadOnlyList<double> Closes,
        IReadOnlyList<(string Name, double?[] Values)> Columns,
        IReadOnlyDictionary<string, SignalDirection[]> Stances)
    {
        public int Count => Timestamps.Count;

        public static SnapshotTable FromEngine(string ticker, IReadOnlyList<Candle> candles, EngineResult result) => new(
            ticker.ToUpperInvariant(),
            candles.Select(c => c.Timestamp).ToList(),
            candles.Select(c => c.Close).ToList(),
            result.Columns,
            result.Stances);
    }
}
