namespace EricoreTech.CandleSharp.Application
{
    /// <summary>An agent verdict that flipped since the previous watch run.</summary>
    public sealed record WatchChange(
        string Ticker, string AgentKey, string AgentDisplayName,
        string From, string To, double Confidence);
}
