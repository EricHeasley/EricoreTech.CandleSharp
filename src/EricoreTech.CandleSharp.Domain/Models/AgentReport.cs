namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>The full output of one agent for one ticker.</summary>
    public sealed record AgentReport(
        string Key,
        string DisplayName,
        string Ticker,
        TradeSignal Signal,
        IReadOnlyList<ScoreCard> Scores);
}
