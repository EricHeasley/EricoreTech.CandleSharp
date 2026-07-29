namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// A trigger point: the bar where an indicator's stance changed to a
    /// directional one. Produced by <see cref="IndicatorEngine"/> with the same
    /// rule for every indicator.
    /// </summary>
    public sealed record Trigger(DateTime Timestamp, string Indicator, SignalDirection Direction);
}
