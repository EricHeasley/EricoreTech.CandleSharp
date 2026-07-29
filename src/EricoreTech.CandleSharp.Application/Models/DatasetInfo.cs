namespace EricoreTech.CandleSharp.Application
{
    /// <summary>One locally stored ticker/interval dataset.</summary>
    public sealed record DatasetInfo(string Ticker, string Interval, string Path);
}
