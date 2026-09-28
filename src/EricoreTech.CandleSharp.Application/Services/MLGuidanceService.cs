using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>
    /// Use case for the gradient-boosted-trees guidance model: build one
    /// ticker's feature snapshot fresh from its stored candles, and train +
    /// walk-forward-grade a model on it. Indicator outputs found to use
    /// future bars (e.g. Ichimoku's Chikou) are excluded, the same guard
    /// <see cref="RuleService"/> and <see cref="PatternService"/> apply.
    /// </summary>
    public sealed class MLGuidanceService(ICandleRepository repository, IPluginCatalog catalog)
    {
        public MLGuidanceReport Analyze(string ticker, string interval, MLGuidanceOptions options)
        {
            var candles = repository.Load(ticker, interval);
            var engine = catalog.CreateEngine();
            var result = engine.Run(candles);
            var (leakyColumns, leakyStances) = LookaheadDetector.Find(engine, candles);
            var leaky = new HashSet<string>(leakyColumns.Concat(leakyStances), StringComparer.OrdinalIgnoreCase);
            var table = SnapshotTable.FromEngine(ticker, candles, result);
            return MLGuidanceAnalyzer.Analyze(table, options, leaky);
        }
    }
}
