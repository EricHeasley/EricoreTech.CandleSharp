using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application {
    /// <summary>
    /// Use cases over the indicator engine and trading agents: series with
    /// signals, agent reports, the composed verdict, the multi-ticker
    /// screener, and the walk-forward backtest.
    /// </summary>
    public sealed class AnalysisService(
        ICandleRepository repository, IPluginCatalog catalog, ISocialRepository socialRepository) {
        private const string RiskManagerKey = "risk_manager";

        public SeriesAnalysis GetSeries(string ticker, string interval) {
            var candles = repository.Load(ticker, interval);
            return new SeriesAnalysis(
                ticker.ToUpperInvariant(), interval, candles, catalog.CreateEngine().Run(candles));
        }

        public IReadOnlyList<AgentReport> Analyze(string ticker, string interval, IReadOnlyList<string>? agentKeys = null) {
            var input = WithSocial(GetSeries(ticker, interval).ToAgentInput());
            var agents = FilterAgents(agentKeys);
            return agents.Select(a => a.Agent.Analyze(input)).ToList();
        }

        public Verdict GetVerdict(string ticker, string interval) {
            var input = WithSocial(GetSeries(ticker, interval).ToAgentInput());
            var reports = catalog.Agents.Select(a => a.Agent.Analyze(input)).ToList();
            return VerdictComposer.Compose(input, reports);
        }

        public IReadOnlyList<ScreenRow> Screen(string? interval = null) {
            var engine = catalog.CreateEngine();
            var directional = catalog.Agents.Where(a => a.Agent.Key != RiskManagerKey).ToList();
            var rows = new List<ScreenRow>();
            foreach (var dataset in repository.List().Where(d => interval is null || d.Interval == interval))
            {
                var candles = repository.Load(dataset.Ticker, dataset.Interval);
                var input = WithSocial(new AgentInput(dataset.Ticker, dataset.Interval, candles, engine.Run(candles)));
                var reports = directional.Select(a => a.Agent.Analyze(input)).ToList();
                var rankBy = reports.FirstOrDefault(r => r.Key == "consensus") ?? reports.FirstOrDefault();
                double rank = rankBy?.Signal.Direction switch
                {
                    SignalDirection.Bullish => rankBy.Signal.Confidence,
                    SignalDirection.Bearish => -rankBy.Signal.Confidence,
                    _ => 0,
                };
                rows.Add(new ScreenRow(dataset.Ticker, dataset.Interval, reports, rank));
            }
            return rows.OrderByDescending(r => r.Rank).ToList();
        }

        public IReadOnlyList<BacktestReport> Backtest(
            string ticker, string interval, BacktestOptions options, IReadOnlyList<string>? agentKeys = null) {
            var candles = repository.Load(ticker, interval);
            var engine = catalog.CreateEngine();
            return FilterAgents(agentKeys)
                .Where(a => a.Agent.Key != RiskManagerKey)
                .Select(a => Backtester.Run(a.Agent, engine, ticker, interval, candles, options))
                .OrderByDescending(r => r.HitRate)
                .ToList();
        }

        private AgentInput WithSocial(AgentInput input) =>
            input with { Social = SocialAnalytics.Summarize(input.Ticker, socialRepository.LoadPosts(input.Ticker)) };

        private IReadOnlyList<RegisteredAgent> FilterAgents(IReadOnlyList<string>? agentKeys) =>
            agentKeys is { Count: > 0 }
                ? catalog.Agents.Where(a => agentKeys.Contains(a.Agent.Key)).ToList()
                : catalog.Agents;

    }
}