using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>Use cases around crowd sentiment: pull fresh chatter, summarize what is stored.</summary>
    public sealed class SocialService(ISocialRepository repository, ISocialFeed feed)
    {
        /// <summary>Fetches the latest posts and merges them into local storage; returns how many arrived.</summary>
        public async Task<int> FetchAndStoreAsync(string ticker)
        {
            var posts = await feed.FetchPostsAsync(ticker);
            if (posts.Count > 0) repository.SavePosts(posts, ticker);
            return posts.Count;
        }

        public SocialSentimentSummary GetSummary(string ticker) =>
            SocialAnalytics.Summarize(ticker.ToUpperInvariant(), repository.LoadPosts(ticker));
    }
}
