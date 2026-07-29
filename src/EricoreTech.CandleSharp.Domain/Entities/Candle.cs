namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>One OHLCV bar. Timestamp is UTC; for daily bars it is the trading date at midnight.</summary>
    public sealed record Candle(
        DateTime Timestamp,
        double Open,
        double High,
        double Low,
        double Close,
        long Volume);
}
