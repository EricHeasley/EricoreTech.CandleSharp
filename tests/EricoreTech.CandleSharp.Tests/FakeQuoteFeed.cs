using EricoreTech.CandleSharp.Application;
using EricoreTech.CandleSharp.Domain;

/// <summary>Scripted feed for testing use cases without a network.</summary>
sealed class FakeQuoteFeed : IQuoteFeed
{
    public Dictionary<string, QuoteHistory> Responses { get; } = [];

    public List<(string Ticker, string Interval)> Requests { get; } = [];

    public Task<QuoteHistory> FetchHistoryAsync(
        string ticker, string period = "1y", string interval = "1d",
        DateOnly? start = null, DateOnly? end = null)
    {
        Requests.Add((ticker.ToUpperInvariant(), interval));
        if (!Responses.TryGetValue(ticker.ToUpperInvariant(), out var history))
            throw new InvalidOperationException($"no scripted data for {ticker}");
        return Task.FromResult(history);
    }
}
