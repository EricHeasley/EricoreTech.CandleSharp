namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// The one contract every technical indicator implements. An indicator never
    /// decides trigger points itself — it only reports its stance per bar, and the
    /// engine turns stance changes into triggers uniformly. To add an indicator:
    /// implement this, register it, done.
    /// </summary>
    public interface ITechnicalIndicator
    {
        /// <summary>Unique name, parameters included, e.g. "RSI_14" or "SMA_cross_20_50".</summary>
        string Name { get; }

        IndicatorResult Compute(IReadOnlyList<Candle> candles);
    }
}
