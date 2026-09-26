namespace EricoreTech.CandleSharp.Application
{
    /// <summary>Outcome of recording one dataset's indicators into the snapshot journal.</summary>
    public sealed record SnapshotResult(
        string Ticker, string Interval, int Bars, int Added, int Indicators, string Path);
}
