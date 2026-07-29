namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// Pure social-sentiment math. Author-tagged posts count directly; untagged
    /// posts are scored with a small finance lexicon and count only when the
    /// score clears a threshold. The bullish ratio is over directional posts.
    /// </summary>
    public static class SocialAnalytics
    {
        private const double LexiconThreshold = 0.15;

        private static readonly HashSet<string> Positive = new(StringComparer.OrdinalIgnoreCase)
        {
            "buy", "bull", "bullish", "long", "calls", "moon", "rocket", "rip", "breakout",
            "beat", "beats", "upgrade", "upgraded", "strong", "growth", "gains", "winner",
            "undervalued", "rally", "surge", "soar", "pump", "accumulate", "dip", "cheap",
        };

        private static readonly HashSet<string> Negative = new(StringComparer.OrdinalIgnoreCase)
        {
            "sell", "bear", "bearish", "short", "puts", "crash", "dump", "tank", "drill",
            "miss", "missed", "downgrade", "downgraded", "weak", "loss", "losses", "bagholder",
            "overvalued", "plunge", "collapse", "fraud", "avoid", "trap", "bubble", "drop",
        };

        /// <summary>Lexicon score for one text: (positive - negative) / matched, in -1..+1; 0 when nothing matches.</summary>
        public static double Score(string text)
        {
            int positive = 0, negative = 0;
            foreach (var token in text.Split(' ', '\t', '\n', ',', '.', '!', '?', ':', ';', '(', ')', '"'))
            {
                var word = token.TrimStart('$', '#', '@');
                if (Positive.Contains(word)) positive++;
                else if (Negative.Contains(word)) negative++;
            }
            int matched = positive + negative;
            return matched == 0 ? 0 : (double)(positive - negative) / matched;
        }

        public static SocialSentimentSummary Summarize(string ticker, IReadOnlyList<SocialPost> posts)
        {
            var sorted = posts.OrderBy(p => p.Timestamp).ToList();
            if (sorted.Count == 0)
                return new SocialSentimentSummary(ticker, 0, 0, 0, 0, 0, null, 0, null, null, []);

            int taggedBullish = 0, taggedBearish = 0, lexiconBullish = 0, lexiconBearish = 0;
            double scoreSum = 0;
            foreach (var post in sorted)
            {
                double score = Score(post.Text);
                scoreSum += score;
                if (post.Tagged == SignalDirection.Bullish) taggedBullish++;
                else if (post.Tagged == SignalDirection.Bearish) taggedBearish++;
                else if (score > LexiconThreshold) lexiconBullish++;
                else if (score < -LexiconThreshold) lexiconBearish++;
            }

            int bullish = taggedBullish + lexiconBullish;
            int bearish = taggedBearish + lexiconBearish;
            double? ratio = bullish + bearish == 0 ? null : 100.0 * bullish / (bullish + bearish);

            return new SocialSentimentSummary(
                ticker, sorted.Count, taggedBullish, taggedBearish, lexiconBullish, lexiconBearish,
                ratio, scoreSum / sorted.Count,
                sorted[0].Timestamp, sorted[^1].Timestamp,
                sorted.TakeLast(8).Reverse().ToList());
        }
    }
}
