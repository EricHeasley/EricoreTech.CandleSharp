using EricoreTech.CandleSharp.Application;

namespace EricoreTech.CandleSharp.Web.Services
{
    /// <summary>
    /// Builds the sidebar watchlist: for every saved dataset, its last close and
    /// day-over-day change, a tiny sparkline, whether any of the user's rules is
    /// firing on it right now, and the consensus agent's current lean. This is
    /// the thing that used to be a wide, sideways-scrolling Screener table;
    /// here it's what the sidebar is built from directly.
    /// </summary>
    public sealed class WatchlistService(ICandleRepository repository, AnalysisService analysis, RuleService rules)
    {
        public IReadOnlyList<WatchlistRow> Build(string interval)
        {
            var datasets = repository.List().Where(d => d.Interval == interval).ToList();
            if (datasets.Count == 0) return [];

            var consensusByTicker = analysis.Screen(interval)
                .ToDictionary(r => r.Ticker, r => r.Reports.FirstOrDefault(x => x.Key == "consensus")?.Signal.Direction.ToString());
            var firingByTicker = rules.EvaluateAll(interval)
                .Where(r => r.FiringNow)
                .GroupBy(r => r.Ticker)
                .ToDictionary(g => g.Key, g => g.Count());

            var rows = new List<WatchlistRow>();
            foreach (var dataset in datasets)
            {
                List<Domain.Candle> candles;
                try
                {
                    candles = repository.Load(dataset.Ticker, dataset.Interval);
                }
                catch (FileNotFoundException)
                {
                    continue;
                }
                if (candles.Count == 0) continue;

                double last = candles[^1].Close;
                double prev = candles.Count > 1 ? candles[^2].Close : last;
                var spark = candles.TakeLast(30).Select(c => c.Close).ToList();
                rows.Add(new WatchlistRow(
                    dataset.Ticker, dataset.Interval, candles.Count, last,
                    prev > 0 ? (last - prev) / prev : 0, spark,
                    firingByTicker.GetValueOrDefault(dataset.Ticker), consensusByTicker.GetValueOrDefault(dataset.Ticker)));
            }
            return rows.OrderBy(r => r.Ticker, StringComparer.Ordinal).ToList();
        }
    }
}
