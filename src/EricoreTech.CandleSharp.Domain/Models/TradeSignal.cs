namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>An agent's verdict: direction, confidence 0-100, and why.</summary>
    public sealed record TradeSignal(
        SignalDirection Direction,
        double Confidence,
        string Reasoning);
}
