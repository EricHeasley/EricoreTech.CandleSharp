using System.Text.Json;
using EricoreTech.CandleSharp.Application;
using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Infrastructure
{
    /// <summary>
    /// Pulls the public StockTwits symbol stream (no API key). Posts carry the
    /// author's own Bullish/Bearish tag when they chose one. The endpoint is
    /// unauthenticated and rate-limited (~200 requests/hour), so refresh
    /// occasionally, not in a loop.
    /// </summary>
    public sealed class StockTwitsClient : ISocialFeed, IDisposable
    {
        private const string BaseUrl = "https://api.stocktwits.com/api/2/streams/symbol/";

        private readonly HttpClient _http;

        public StockTwitsClient()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        }

        public async Task<List<SocialPost>> FetchPostsAsync(string ticker)
        {
            var url = $"{BaseUrl}{Uri.EscapeDataString(ticker.ToUpperInvariant())}.json";
            using var response = await _http.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"StockTwits returned {(int)response.StatusCode} for {ticker}");

            var posts = new List<SocialPost>();
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("messages", out var messages))
                return posts;

            foreach (var message in messages.EnumerateArray())
            {
                if (!message.TryGetProperty("body", out var text)
                    || !message.TryGetProperty("created_at", out var created))
                    continue;

                var author = message.TryGetProperty("user", out var user)
                    && user.TryGetProperty("username", out var username)
                    ? username.GetString() ?? "unknown"
                    : "unknown";

                var tagged = SignalDirection.Neutral;
                if (message.TryGetProperty("entities", out var entities)
                    && entities.TryGetProperty("sentiment", out var sentiment)
                    && sentiment.ValueKind == JsonValueKind.Object
                    && sentiment.TryGetProperty("basic", out var basic))
                    tagged = basic.GetString() switch
                    {
                        "Bullish" => SignalDirection.Bullish,
                        "Bearish" => SignalDirection.Bearish,
                        _ => SignalDirection.Neutral,
                    };

                posts.Add(new SocialPost(
                    DateTime.Parse(created.GetString()!, null,
                        System.Globalization.DateTimeStyles.AdjustToUniversal),
                    author, text.GetString() ?? "", tagged));
            }
            return posts;
        }

        public void Dispose() => _http.Dispose();
    }
}
