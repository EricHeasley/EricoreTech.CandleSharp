namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>One cash dividend payment (ex-dividend date and per-share amount).</summary>
    public sealed record Dividend(DateTime Timestamp, double Amount);
}
