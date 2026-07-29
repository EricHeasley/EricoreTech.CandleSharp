using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Agents
{
    /// <summary>
    /// Crowd sentiment from stored social posts. Deliberately conservative:
    /// Neutral without enough recent chatter, and directional only when the
    /// bullish ratio clears a real threshold. Social data has no history, so
    /// this agent reports Neutral inside backtests (hosts pass no social
    /// payload there) rather than pretending it knew past sentiment.
    /// </summary>
    public sealed class SentimentAgent(
        int minPosts = 10, double bullishAbove = 60, double bearishBelow = 40, int maxAgeDays = 7)
        : ITradingAgent
    {
        public string Key => "sentiment";
        public string DisplayName => "Crowd Sentiment";

        public AgentReport Analyze(AgentInput input)
        {
            var social = input.Social;
            if (social is null || social.PostCount == 0)
                return Report(input, new TradeSignal(SignalDirection.Neutral, 0,
                    "No social posts stored. Pull chatter with the social command or dashboard button."), null);

            var details = new List<string>
            {
                $"{social.PostCount} posts from {social.OldestPost:yyyy-MM-dd} to {social.NewestPost:yyyy-MM-dd}",
                $"Author-tagged: {social.TaggedBullish} bullish vs {social.TaggedBearish} bearish",
                $"Lexicon-read (untagged posts): {social.LexiconBullish} bullish vs {social.LexiconBearish} bearish",
                $"Average lexicon score {social.AverageLexiconScore:+0.00;-0.00} (-1 bearish .. +1 bullish)",
            };

            if (social.PostCount < minPosts)
                return Report(input, new TradeSignal(SignalDirection.Neutral, 0,
                    $"Only {social.PostCount} posts stored; need {minPosts} before taking the crowd seriously."), details);

            var asOf = input.Candles.Count > 0 ? input.Candles[^1].Timestamp : DateTime.UtcNow;
            if (social.NewestPost is { } newest && (asOf - newest).TotalDays > maxAgeDays)
                return Report(input, new TradeSignal(SignalDirection.Neutral, 0,
                    $"Stored chatter is stale (newest post {newest:yyyy-MM-dd}); refresh before trusting it."), details);

            if (social.BullishRatioPercent is not { } ratio)
                return Report(input, new TradeSignal(SignalDirection.Neutral, 0,
                    "Posts exist but none read directional."), details);

            var direction = ratio > bullishAbove ? SignalDirection.Bullish
                : ratio < bearishBelow ? SignalDirection.Bearish
                : SignalDirection.Neutral;
            double confidence = Math.Round(Math.Min(100, Math.Abs(ratio - 50) * 2));
            return Report(input, new TradeSignal(direction, direction == SignalDirection.Neutral ? 0 : confidence,
                $"{ratio:0}% of directional posts lean bullish across {social.PostCount} recent posts."), details);
        }

        private AgentReport Report(AgentInput input, TradeSignal signal, List<string>? details)
        {
            var social = input.Social;
            double score = social is null ? 0 : (social.TaggedBullish + social.LexiconBullish)
                - (social.TaggedBearish + social.LexiconBearish);
            return new AgentReport(Key, DisplayName, input.Ticker, signal,
                details is null ? [] : [new ScoreCard("Crowd tally", score, social?.PostCount ?? 0, details)]);
        }
    }
}
