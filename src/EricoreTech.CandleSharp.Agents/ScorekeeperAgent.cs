using System.Globalization;
using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Agents
{
    /// <summary>
    /// Weights the consensus by evidence: for every indicator, measures how price
    /// actually moved after its past triggers on THIS ticker, then lets only
    /// indicators with a track record vote, each weighted by its hit rate.
    /// (Adaptation of ai-hedge-fund-net's base-rate grounding idea.)
    /// </summary>
    public sealed class ScorekeeperAgent(int horizon = 10, int minTriggers = 3) : ITradingAgent
    {
        public string Key => "scorekeeper";
        public string DisplayName => "Scorekeeper";

        public AgentReport Analyze(AgentInput input)
        {
            var candles = input.Candles;
            var indexOf = new Dictionary<DateTime, int>();
            for (int i = 0; i < candles.Count; i++) indexOf[candles[i].Timestamp] = i;

            var details = new List<string>();
            double bullWeight = 0, bearWeight = 0;
            int rated = 0;

            foreach (var group in input.Signals.Triggers.GroupBy(t => t.Indicator).OrderBy(g => g.Key))
            {
                int evaluated = 0, hits = 0;
                double alignedReturnSum = 0;
                foreach (var trigger in group)
                {
                    if (!indexOf.TryGetValue(trigger.Timestamp, out int i) || i + horizon >= candles.Count)
                        continue;
                    double forward = (candles[i + horizon].Close - candles[i].Close) / candles[i].Close;
                    double aligned = trigger.Direction == SignalDirection.Bullish ? forward : -forward;
                    evaluated++;
                    if (aligned > 0) hits++;
                    alignedReturnSum += aligned;
                }
                if (evaluated < minTriggers)
                {
                    if (evaluated > 0)
                        details.Add($"{group.Key}: only {evaluated} evaluable trigger(s), not rated (need {minTriggers})");
                    continue;
                }

                double hitRate = (double)hits / evaluated;
                double avgAligned = alignedReturnSum / evaluated;
                rated++;
                details.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{group.Key}: {hits}/{evaluated} triggers aligned ({hitRate:P0}), avg aligned move {avgAligned:+0.0%;-0.0%} over {horizon} bars"));

                if (!input.Signals.Stances.TryGetValue(group.Key, out var stance)) continue;
                var (direction, _) = Stances.LatestRun(stance);
                if (direction == SignalDirection.Bullish) bullWeight += hitRate;
                else if (direction == SignalDirection.Bearish) bearWeight += hitRate;
            }

            TradeSignal signal;
            double totalWeight = bullWeight + bearWeight;
            if (rated == 0)
                signal = new TradeSignal(SignalDirection.Neutral, 0,
                    $"No indicator has {minTriggers}+ evaluable triggers in the loaded history yet.");
            else if (totalWeight == 0)
                signal = new TradeSignal(SignalDirection.Neutral, 0,
                    $"{rated} indicators have track records but none holds a directional stance right now.");
            else
            {
                var direction = bullWeight > bearWeight ? SignalDirection.Bullish
                    : bearWeight > bullWeight ? SignalDirection.Bearish
                    : SignalDirection.Neutral;
                double confidence = Math.Round(100 * Math.Abs(bullWeight - bearWeight) / totalWeight);
                signal = new TradeSignal(direction, confidence, string.Create(CultureInfo.InvariantCulture,
                    $"Hit-rate-weighted vote: bullish {bullWeight:0.00} vs bearish {bearWeight:0.00} across {rated} indicators with track records ({horizon}-bar horizon)."));
            }

            return new AgentReport(Key, DisplayName, input.Ticker, signal,
                [new ScoreCard($"Track records ({horizon}-bar horizon)", bullWeight - bearWeight, rated, details)]);
        }
    }
}
