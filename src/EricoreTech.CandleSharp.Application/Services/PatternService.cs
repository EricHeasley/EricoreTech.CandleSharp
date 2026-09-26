using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>
    /// Use cases for the indicator snapshot journal: record every indicator's
    /// values and stances per bar, then mine the journal for conditions that
    /// preceded unusual forward performance.
    /// </summary>
    public sealed class PatternService(
        ICandleRepository repository, ISnapshotRepository snapshots, IPluginCatalog catalog)
    {
        public SnapshotResult Record(string ticker, string interval)
        {
            var candles = repository.Load(ticker, interval);
            var table = SnapshotTable.FromEngine(ticker, candles, catalog.CreateEngine().Run(candles));
            int added = snapshots.SaveSnapshots(table, interval);
            var stored = snapshots.LoadSnapshots(ticker, interval)!;
            return new SnapshotResult(
                stored.Ticker, interval, stored.Count, added, stored.Stances.Count,
                snapshots.SnapshotPathFor(ticker, interval));
        }

        /// <summary>Records every saved dataset (optionally one interval only).</summary>
        public IReadOnlyList<SnapshotResult> RecordAll(string? interval = null) =>
            repository.List()
                .Where(d => interval is null || d.Interval == interval)
                .Select(d => Record(d.Ticker, d.Interval))
                .ToList();

        /// <summary>
        /// Brings each ticker's journal up to date with its candles, then analyzes
        /// the stored journals together (pooled) against forward returns. Indicator
        /// outputs found to use future bars are excluded from the analysis.
        /// </summary>
        public PatternReport Analyze(IReadOnlyList<string> tickers, string interval, PatternOptions options)
        {
            if (tickers.Count == 0)
                tickers = repository.List().Where(d => d.Interval == interval).Select(d => d.Ticker).ToList();
            if (tickers.Count == 0)
                throw new InvalidOperationException($"No saved {interval} datasets to analyze. Fetch some tickers first.");

            var engine = catalog.CreateEngine();
            var tables = new List<SnapshotTable>();
            var leakyColumns = new HashSet<string>();
            var leakyStances = new HashSet<string>();
            foreach (var ticker in tickers)
            {
                Record(ticker, interval);
                tables.Add(snapshots.LoadSnapshots(ticker, interval)!);
                var (columns, stances) = LookaheadDetector.Find(engine, repository.Load(ticker, interval));
                leakyColumns.UnionWith(columns);
                leakyStances.UnionWith(stances);
            }
            return PatternAnalyzer.Analyze(tables, options, leakyColumns, leakyStances);
        }
    }
}
