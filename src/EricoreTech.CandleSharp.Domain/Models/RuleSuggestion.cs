namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// Settings for <see cref="RuleSuggester"/>. Candidates are scored on the
    /// older part of history; the last TestFraction is held back to check the
    /// winners. MinSignals is the fewest past signals a rule needs in the
    /// discovery period to be considered at all.
    /// </summary>
    public sealed record SuggestOptions(
        int Horizon = 10,
        int MinSignals = 8,
        double TestFraction = 0.3,
        int Top = 5,
        bool IncludePairs = true)
    {
        public void Validate()
        {
            if (Horizon < 1) throw new ArgumentOutOfRangeException(nameof(Horizon), "horizon must be at least 1 bar");
            if (MinSignals < 2) throw new ArgumentOutOfRangeException(nameof(MinSignals), "min signals must be at least 2");
            if (TestFraction is <= 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(TestFraction), "test fraction must be in (0, 1)");
            if (Top < 1) throw new ArgumentOutOfRangeException(nameof(Top), "top must be at least 1");
        }
    }

    /// <summary>
    /// How a rule's signals did over one period, in the rule's direction.
    /// HitRate = share of signals where the stock moved the right way;
    /// Edge = average direction-aligned return beyond a typical bar of the same
    /// period; TScore counts only non-overlapping signals as independent.
    /// </summary>
    public sealed record SignalStats(int Signals, double AvgReturn, double HitRate, double Edge, double TScore);

    /// <summary>
    /// How much to trust a suggestion. Strong: its discovery score beats what
    /// the best of that many random rules would reach by luck, AND it clearly
    /// held up on recent data. Promising: one of the two. Weak: neither — it may
    /// well be luck.
    /// </summary>
    public enum SuggestionStrength
    {
        Weak,
        Promising,
        Strong,
    }

    /// <summary>
    /// A rule proposed from past performance. Discovery = the older history it
    /// was chosen on; Recent = the held-back recent history it was checked on
    /// (null when it never fired there). HeldUp is true when it still beat a
    /// typical stretch on the recent data (null when it fired too rarely there
    /// to judge).
    /// </summary>
    public sealed record RuleSuggestion(
        TradeRule Rule,
        SignalStats Discovery,
        SignalStats? Recent,
        bool? HeldUp,
        bool FiringNow,
        SuggestionStrength Strength);

    /// <summary>
    /// Best buy and sell rules found for one ticker. Tested is how many
    /// candidate rules were scored — the more tried, the more that look good by
    /// luck. LuckBar is roughly the t-score the best of that many pure-noise
    /// rules would reach, the bar a Strong suggestion has to clear.
    /// </summary>
    public sealed record SuggestionReport(
        string Ticker,
        SuggestOptions Options,
        DateTime DiscoveryEnd,
        int Tested,
        double LuckBar,
        IReadOnlyList<RuleSuggestion> Buy,
        IReadOnlyList<RuleSuggestion> Sell);
}
