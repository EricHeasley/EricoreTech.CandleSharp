using System.Globalization;
using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application {
    /// <summary>
    /// Watch mode: optionally refresh data, run every agent, diff verdicts
    /// against the previous run's stored state, and report only the flips.
    /// </summary>
    public sealed class WatchService(
        ICandleRepository repository,
        IDividendRepository dividendRepository,
        ISocialRepository socialRepository,
        IPluginCatalog catalog,
        IAgentStateStore stateStore,
        IQuoteFeed feed) {
        public async Task<WatchResult> RunAsync(IReadOnlyList<string> tickers, string interval, bool refresh) {
            var warnings = new List<string>();
            if (tickers.Count == 0)
                tickers = repository.List().Where(d => d.Interval == interval).Select(d => d.Ticker).ToList();

            if (refresh)
                foreach (var ticker in tickers)
                {
                    try
                    {
                        var fresh = await feed.FetchHistoryAsync(ticker, "3mo", interval);
                        repository.Save(fresh.Candles, ticker, interval);
                        if (fresh.Dividends.Count > 0)
                            dividendRepository.SaveDividends(fresh.Dividends, ticker);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"{ticker}: refresh failed, using saved data ({ex.Message})");
                    }
                }

            var engine = catalog.CreateEngine();
            var previous = stateStore.Load(interval);
            var current = new Dictionary<string, Dictionary<string, string>>();
            var changes = new List<WatchChange>();

            foreach (var ticker in tickers)
            {
                List<Candle> candles;
                try
                {
                    candles = repository.Load(ticker, interval);
                }
                catch (FileNotFoundException)
                {
                    warnings.Add($"{ticker}: no local data, skipping");
                    continue;
                }
                var key = ticker.ToUpperInvariant();
                var input = new AgentInput(key, interval, candles, engine.Run(candles))
                {
                    Social = SocialAnalytics.Summarize(key, socialRepository.LoadPosts(key)),
                };

                var verdicts = new Dictionary<string, string>();
                foreach (var registered in catalog.Agents)
                {
                    var report = registered.Agent.Analyze(input);
                    verdicts[report.Key] = string.Create(CultureInfo.InvariantCulture,
                        $"{report.Signal.Direction}|{report.Signal.Confidence:0}");
                    var was = previous.GetValueOrDefault(key)?.GetValueOrDefault(report.Key);
                    if (was is not null && was.Split('|')[0] != report.Signal.Direction.ToString())
                        changes.Add(new WatchChange(
                            key, report.Key, report.DisplayName,
                            was.Split('|')[0], report.Signal.Direction.ToString(), report.Signal.Confidence));
                }
                current[key] = verdicts;
            }

            stateStore.Save(interval, current);
            return new WatchResult(changes, current.Count, warnings);
        }
    }
}