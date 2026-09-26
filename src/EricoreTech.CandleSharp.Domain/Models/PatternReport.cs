namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// How the stock performed over the next Horizon bars whenever a condition
    /// held. In-sample numbers come from the older part of history (where
    /// patterns are discovered); Oos* numbers from the held-out recent part
    /// (where they are checked). Edge = average forward return minus the
    /// baseline average of the same period, so a positive edge means the
    /// condition beat simply holding the stock.
    /// </summary>
    public sealed record PatternStat(
        string Label,
        string Kind,
        int Samples,
        double AvgReturn,
        double WinRate,
        double Edge,
        double TScore,
        int OosSamples,
        double? OosAvgReturn,
        double? OosWinRate,
        double? OosEdge,
        bool? HoldsOutOfSample,
        IReadOnlyList<string> ActiveIn);

    /// <summary>Forward-return baseline over every bar of one period.</summary>
    public sealed record BaselineStat(int Samples, double AvgReturn, double WinRate);

    /// <summary>
    /// One numeric indicator column split into buckets (quantiles of each
    /// ticker's own in-sample values) plus its rank correlation with the forward
    /// return. RelativeToClose marks price-level columns (moving averages,
    /// bands, stops) that were analyzed as distance from the close.
    /// </summary>
    public sealed record ColumnStudy(
        string Column,
        bool RelativeToClose,
        double? Correlation,
        double? OosCorrelation,
        IReadOnlyList<PatternStat> Buckets);

    /// <summary>
    /// Result of mining stored indicator snapshots for conditions that preceded
    /// unusual performance. Tested/Notable/ChanceExpected put the findings in
    /// context: with many conditions tested, some clear |t| ≥ 2 by luck alone,
    /// which is why the out-of-sample check matters. Excluded names the columns
    /// and stances left out because they use future bars (see LookaheadDetector).
    /// </summary>
    public sealed record PatternReport(
        IReadOnlyList<string> Tickers,
        PatternOptions Options,
        int Bars,
        BaselineStat Baseline,
        BaselineStat OosBaseline,
        int Tested,
        int Notable,
        int NotableChecked,
        int NotableHeld,
        double ChanceExpected,
        IReadOnlyList<PatternStat> BullishEdges,
        IReadOnlyList<PatternStat> BearishEdges,
        IReadOnlyList<PatternStat> ActiveNow,
        IReadOnlyList<ColumnStudy> ColumnStudies,
        IReadOnlyList<string> Excluded);
}
