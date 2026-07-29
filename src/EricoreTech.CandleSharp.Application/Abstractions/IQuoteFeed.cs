using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>Port for a market-data source.</summary>
    public interface IQuoteFeed
    {
        Task<QuoteHistory> FetchHistoryAsync(
            string ticker,
            string period = "1y",
            string interval = "1d",
            DateOnly? start = null,
            DateOnly? end = null);
    }
}
