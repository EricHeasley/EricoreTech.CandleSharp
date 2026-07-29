using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>One dataset touched by a refresh-all run. Mode is "fetched" (from the feed) or "derived" (resampled locally).</summary>
    public sealed record RefreshedDataset(string Ticker, string Interval, int Bars, int Dividends, string Mode);
}
