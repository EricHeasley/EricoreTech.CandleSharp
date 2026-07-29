namespace EricoreTech.CandleSharp.Web
{
    /// <summary>Body of POST /api/fetch.</summary>
    internal sealed record FetchRequest(string? Ticker, string? Period, string? Interval, string? Start, string? End);
}
