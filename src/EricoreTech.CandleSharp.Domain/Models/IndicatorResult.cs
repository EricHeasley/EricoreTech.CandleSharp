namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// Everything an indicator produces for a candle series: its output columns
    /// (for the CSV) and its per-bar stance (for signals). Both must be aligned
    /// to the input, one entry per candle.
    /// </summary>
    public sealed record IndicatorResult(
        IReadOnlyList<(string Name, double?[] Values)> Columns,
        SignalDirection[] Stance);
}
