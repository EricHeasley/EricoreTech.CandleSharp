namespace EricoreTech.CandleSharp.Application
{
    /// <summary>Outcome of deriving a coarser timeframe from stored data.</summary>
    public sealed record ResampleResult(
        string Ticker, string SourceInterval, int SourceBars,
        string TargetInterval, int TargetBars, string Path);
}
