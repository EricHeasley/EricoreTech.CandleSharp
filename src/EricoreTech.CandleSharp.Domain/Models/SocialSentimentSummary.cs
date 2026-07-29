namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>Crowd read on a ticker from stored social posts.</summary>
    public sealed record SocialSentimentSummary(
        string Ticker,
        int PostCount,
        int TaggedBullish,
        int TaggedBearish,
        int LexiconBullish,
        int LexiconBearish,
        double? BullishRatioPercent,
        double AverageLexiconScore,
        DateTime? OldestPost,
        DateTime? NewestPost,
        IReadOnlyList<SocialPost> RecentPosts);
}
