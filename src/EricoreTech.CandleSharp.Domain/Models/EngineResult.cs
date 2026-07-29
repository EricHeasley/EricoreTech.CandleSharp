namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// Combined output of an engine run: all indicator columns (deduplicated,
    /// candle-aligned), all triggers in chronological order, and each indicator's
    /// full per-bar stance series keyed by indicator name.
    /// </summary>
    public sealed record EngineResult(
        IReadOnlyList<(string Name, double?[] Values)> Columns,
        IReadOnlyList<Trigger> Triggers,
        IReadOnlyDictionary<string, SignalDirection[]> Stances);
}
