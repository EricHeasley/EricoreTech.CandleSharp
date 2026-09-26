namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>One time a rule fired: the first bar of a run where all its conditions held.</summary>
    public sealed record RuleSignal(DateTime Timestamp, double Close, double? ForwardReturn);

    /// <summary>
    /// A rule checked against one ticker's history. FiringNow = every condition
    /// holds on the latest bar; NewSignal = it only just started holding there.
    /// Past signals are scored over the rule's horizon: HitRate counts moves in
    /// the rule's direction (up for Buy, down for Sell), and Edge is how much
    /// better than an average bar the signal did in that direction.
    /// Error is set (and everything else empty) when the rule can't run here,
    /// e.g. it names an indicator this data doesn't have.
    /// </summary>
    public sealed record RuleResult(
        TradeRule Rule,
        string Ticker,
        DateTime? AsOf,
        bool FiringNow,
        bool NewSignal,
        int ActiveBars,
        IReadOnlyList<RuleSignal> Signals,
        int Scored,
        double? AvgReturn,
        double? HitRate,
        double? Edge,
        double? Best,
        double? Worst,
        double BaselineReturn,
        string? Error = null)
    {
        public DateTime? LastSignal => Signals.Count > 0 ? Signals[^1].Timestamp : null;

        public static RuleResult Failed(TradeRule rule, string ticker, string error) =>
            new(rule, ticker, null, false, false, 0, [], 0, null, null, null, null, null, 0, error);
    }
}
