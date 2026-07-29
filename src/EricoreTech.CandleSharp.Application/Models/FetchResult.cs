namespace EricoreTech.CandleSharp.Application
{
    /// <summary>Outcome of fetching and storing one ticker's history.</summary>
    public sealed record FetchResult(string Ticker, string Interval, int Bars, string Path, int Dividends = 0);
}
