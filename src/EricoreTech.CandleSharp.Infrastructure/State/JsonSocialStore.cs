using System.Text.Json;
using EricoreTech.CandleSharp.Application;
using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Infrastructure
{
    /// <summary>Persists social posts as data/TICKER_social.json, merged by (timestamp, author).</summary>
    public sealed class JsonSocialStore(string dataDirectory) : ISocialRepository
    {
        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

        private string PathFor(string ticker) =>
            Path.Combine(dataDirectory, $"{ticker.ToUpperInvariant()}_social.json");

        public void SavePosts(IReadOnlyList<SocialPost> posts, string ticker)
        {
            Directory.CreateDirectory(dataDirectory);
            var merged = new SortedDictionary<string, SocialPost>(StringComparer.Ordinal);
            foreach (var post in LoadPosts(ticker)) merged[KeyOf(post)] = post;
            foreach (var post in posts) merged[KeyOf(post)] = post;
            File.WriteAllText(PathFor(ticker), JsonSerializer.Serialize(merged.Values, Options));
        }

        public List<SocialPost> LoadPosts(string ticker)
        {
            var path = PathFor(ticker);
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<SocialPost>>(File.ReadAllText(path)) ?? [];
        }

        private static string KeyOf(SocialPost post) => $"{post.Timestamp:O}|{post.Author}";
    }
}
