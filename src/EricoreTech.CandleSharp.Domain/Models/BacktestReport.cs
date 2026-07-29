namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// One agent's honesty report: at each checkpoint the agent saw only history
    /// up to that bar; its verdict was then scored against the actual forward
    /// return. Aligned = a directional verdict that moved the right way.
    /// </summary>
    public sealed record BacktestReport(
        string AgentKey,
        string DisplayName,
        string Ticker,
        string Interval,
        int Checkpoints,
        int Directional,
        int Aligned,
        double HitRate,
        double AvgAlignedReturn,
        double CumulativeAlignedReturn);
}
