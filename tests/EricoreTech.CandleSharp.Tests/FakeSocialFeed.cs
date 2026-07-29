using EricoreTech.CandleSharp.Application;
using EricoreTech.CandleSharp.Domain;

/// <summary>Scripted social feed for testing use cases without a network.</summary>
sealed class FakeSocialFeed : ISocialFeed
{
    public Dictionary<string, List<SocialPost>> Responses { get; } = [];

    public Task<List<SocialPost>> FetchPostsAsync(string ticker)
    {
        if (!Responses.TryGetValue(ticker.ToUpperInvariant(), out var posts))
            throw new InvalidOperationException($"no scripted chatter for {ticker}");
        return Task.FromResult(posts);
    }
}
