namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// One scored category of analysis. Score is the net directional vote
    /// (bullish minus bearish contributors, so it can be negative); MaxScore is
    /// the number of contributors. Details are human-readable line items.
    /// </summary>
    public sealed record ScoreCard(
        string Title,
        double Score,
        double MaxScore,
        IReadOnlyList<string> Details);
}
