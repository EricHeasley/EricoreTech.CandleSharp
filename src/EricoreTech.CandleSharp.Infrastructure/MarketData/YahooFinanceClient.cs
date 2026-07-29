using System.Text.Json;
using EricoreTech.CandleSharp.Application;
using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Infrastructure
{
    /// <summary>
    /// Pulls free historical data from Yahoo Finance's public chart API.
    /// No API key required; Yahoo only insists on a browser-like User-Agent.
    /// </summary>
    public sealed class YahooFinanceClient : IQuoteFeed, IDisposable
    {
        private const string BaseUrl = "https://query1.finance.yahoo.com/v8/finance/chart/";

        private readonly HttpClient _http;

        public YahooFinanceClient()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        }

        /// <summary>
        /// Fetch OHLCV history for one symbol. Prices are adjusted for splits and
        /// dividends (same as yfinance auto_adjust). Pass either a period like
        /// "1y"/"6mo"/"max", or explicit start/end dates which take precedence.
        /// Intraday intervals ("1m".."1h") are limited by Yahoo to recent history.
        /// </summary>
        public async Task<QuoteHistory> FetchHistoryAsync(
            string symbol,
            string period = "1y",
            string interval = "1d",
            DateOnly? start = null,
            DateOnly? end = null)
        {
            var url = $"{BaseUrl}{Uri.EscapeDataString(symbol)}?interval={Uri.EscapeDataString(interval)}&events=div";
            if (start is not null)
            {
                var period1 = new DateTimeOffset(start.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                var period2 = end is not null
                    ? new DateTimeOffset(end.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                    : DateTimeOffset.UtcNow;
                url += $"&period1={period1.ToUnixTimeSeconds()}&period2={period2.ToUnixTimeSeconds()}";
            }
            else
            {
                url += $"&range={Uri.EscapeDataString(period)}";
            }

            using var response = await _http.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Yahoo returned {(int)response.StatusCode} for {symbol}: {Truncate(body, 200)}");

            return ParseChartResponse(symbol, body, interval);
        }

        private static QuoteHistory ParseChartResponse(string symbol, string json, string interval)
        {
            using var doc = JsonDocument.Parse(json);
            var chart = doc.RootElement.GetProperty("chart");

            if (chart.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                throw new InvalidOperationException(
                    $"Yahoo error for {symbol}: {error.GetProperty("description").GetString()}");

            var results = chart.GetProperty("result");
            if (results.GetArrayLength() == 0)
                throw new InvalidOperationException($"No data returned for {symbol}");

            var result = results[0];
            if (!result.TryGetProperty("timestamp", out var timestamps))
                throw new InvalidOperationException($"No bars returned for {symbol} (empty range?)");

            var quote = result.GetProperty("indicators").GetProperty("quote")[0];
            var opens = quote.GetProperty("open");
            var highs = quote.GetProperty("high");
            var lows = quote.GetProperty("low");
            var closes = quote.GetProperty("close");
            var volumes = quote.GetProperty("volume");

            // adjclose is only present for daily-and-coarser data.
            JsonElement adjCloses = default;
            bool hasAdjClose = result.GetProperty("indicators").TryGetProperty("adjclose", out var adj)
                && adj[0].TryGetProperty("adjclose", out adjCloses);

            bool intraday = interval.EndsWith('m') || interval.EndsWith('h');
            var candles = new List<Candle>(timestamps.GetArrayLength());

            for (int i = 0; i < timestamps.GetArrayLength(); i++)
            {
                // Yahoo pads sessions with null bars (holidays, halted stocks); skip them.
                if (closes[i].ValueKind == JsonValueKind.Null || opens[i].ValueKind == JsonValueKind.Null)
                    continue;

                double open = opens[i].GetDouble();
                double high = highs[i].GetDouble();
                double low = lows[i].GetDouble();
                double close = closes[i].GetDouble();
                long volume = volumes[i].ValueKind == JsonValueKind.Null ? 0 : volumes[i].GetInt64();

                // Scale OHLC by adjclose/close so history is split/dividend adjusted.
                if (hasAdjClose && adjCloses[i].ValueKind != JsonValueKind.Null && close != 0)
                {
                    double factor = adjCloses[i].GetDouble() / close;
                    open *= factor;
                    high *= factor;
                    low *= factor;
                    close *= factor;
                }

                var utc = DateTimeOffset.FromUnixTimeSeconds(timestamps[i].GetInt64()).UtcDateTime;
                var stamp = intraday ? utc : utc.Date;
                candles.Add(new Candle(stamp, open, high, low, close, volume));
            }

            if (candles.Count == 0)
                throw new InvalidOperationException($"No usable bars returned for {symbol}");
            return new QuoteHistory(candles, ParseDividends(result));
        }

        private static List<Dividend> ParseDividends(JsonElement result)
        {
            var dividends = new List<Dividend>();
            if (!result.TryGetProperty("events", out var events)
                || !events.TryGetProperty("dividends", out var payments))
                return dividends;

            foreach (var payment in payments.EnumerateObject())
            {
                if (!payment.Value.TryGetProperty("amount", out var amount)
                    || !payment.Value.TryGetProperty("date", out var date))
                    continue;
                dividends.Add(new Dividend(
                    DateTimeOffset.FromUnixTimeSeconds(date.GetInt64()).UtcDateTime.Date,
                    amount.GetDouble()));
            }
            dividends.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            return dividends;
        }

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : s[..max] + "...";

        public void Dispose() => _http.Dispose();
    }
}
