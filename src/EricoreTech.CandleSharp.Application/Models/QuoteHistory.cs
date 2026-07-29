using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>What one feed request yields: price bars plus any dividend events in the range.</summary>
    public sealed record QuoteHistory(List<Candle> Candles, List<Dividend> Dividends);
}
