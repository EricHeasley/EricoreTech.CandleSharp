namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>The final, human-readable answer for one ticker.</summary>
    public sealed record Verdict(
        SignalDirection Direction,
        double Confidence,
        string Headline,
        IReadOnlyList<string> Details);
}
