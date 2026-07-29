using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>Use cases around getting and shaping market data.</summary>
    public sealed class MarketDataService(
        ICandleRepository repository,
        IDividendRepository dividendRepository,
        ISocialRepository socialRepository,
        ISocialFeed socialFeed,
        IQuoteFeed feed)
    {
        public async Task<FetchResult> FetchAsync(
            string ticker, string period, string interval, DateOnly? start = null, DateOnly? end = null)
        {
            var history = await feed.FetchHistoryAsync(ticker.Trim(), period, interval, start, end);
            var path = repository.Save(history.Candles, ticker.Trim(), interval);
            if (history.Dividends.Count > 0)
                dividendRepository.SaveDividends(history.Dividends, ticker.Trim());
            return new FetchResult(
                ticker.Trim().ToUpperInvariant(), interval, history.Candles.Count, path, history.Dividends.Count);
        }

        public ResampleResult Resample(string ticker, string sourceInterval, string targetInterval)
        {
            if (Resampler.FromInterval(targetInterval) is not { } period)
                throw new ArgumentException($"Unsupported target interval '{targetInterval}' (use 1wk or 1mo).");
            var source = repository.Load(ticker, sourceInterval);
            var aggregated = Resampler.Aggregate(source, period);
            var path = repository.Save(aggregated, ticker, targetInterval);
            return new ResampleResult(
                ticker.ToUpperInvariant(), sourceInterval, source.Count,
                targetInterval, aggregated.Count, path);
        }

        public IReadOnlyList<DatasetInfo> ListDatasets() => repository.List();

        /// <summary>
        /// Refresh every saved dataset: fetchable intervals get a recent-window
        /// top-up from the feed (merged into existing history, dividends
        /// included); resampled weekly/monthly datasets that have a daily
        /// sibling are re-derived locally instead of re-downloaded; and the
        /// latest social chatter is pulled once per ticker and merged into
        /// local storage. Any failure becomes a warning, never a stop.
        /// </summary>
        public async Task<RefreshResult> RefreshAllAsync(string period = "3mo")
        {
            var refreshed = new List<RefreshedDataset>();
            var warnings = new List<string>();
            var datasets = repository.List();

            bool IsDerived(DatasetInfo dataset) =>
                Resampler.FromInterval(dataset.Interval) is not null
                && datasets.Any(d => d.Ticker == dataset.Ticker && d.Interval == "1d");

            foreach (var dataset in datasets.Where(d => !IsDerived(d)))
            {
                try
                {
                    var result = await FetchAsync(dataset.Ticker, period, dataset.Interval);
                    refreshed.Add(new RefreshedDataset(
                        result.Ticker, result.Interval, result.Bars, result.Dividends, "fetched"));
                }
                catch (Exception ex)
                {
                    warnings.Add($"{dataset.Ticker} ({dataset.Interval}): refresh failed ({ex.Message})");
                }
            }

            foreach (var dataset in datasets.Where(IsDerived))
            {
                try
                {
                    var result = Resample(dataset.Ticker, "1d", dataset.Interval);
                    refreshed.Add(new RefreshedDataset(
                        result.Ticker, result.TargetInterval, result.TargetBars, 0, "derived"));
                }
                catch (Exception ex)
                {
                    warnings.Add($"{dataset.Ticker} ({dataset.Interval}): re-derive failed ({ex.Message})");
                }
            }

            var socialPulls = new List<SocialPull>();
            foreach (var ticker in datasets.Select(d => d.Ticker.ToUpperInvariant()).Distinct())
            {
                try
                {
                    var posts = await socialFeed.FetchPostsAsync(ticker);
                    if (posts.Count > 0) socialRepository.SavePosts(posts, ticker);
                    socialPulls.Add(new SocialPull(ticker, posts.Count));
                }
                catch (Exception ex)
                {
                    warnings.Add($"{ticker}: social pull failed ({ex.Message})");
                }
            }

            return new RefreshResult(refreshed, socialPulls, warnings);
        }

        /// <summary>Dividend profile from stored data: yield vs the latest daily close, plus growth history.</summary>
        public DividendSummary GetDividendSummary(string ticker)
        {
            var dividends = dividendRepository.LoadDividends(ticker);
            List<Candle> candles;
            try
            {
                candles = repository.Load(ticker, "1d");
            }
            catch (FileNotFoundException)
            {
                candles = [];
            }
            return DividendAnalytics.Summarize(ticker.ToUpperInvariant(), dividends, candles);
        }
    }
}
