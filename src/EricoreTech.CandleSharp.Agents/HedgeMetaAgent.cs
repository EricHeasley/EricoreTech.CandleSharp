using System.Globalization;
using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Agents
{
    /// <summary>
    /// Online meta-agent (Hedge / multiplicative weights): instead of predicting
    /// price, it learns WHICH of the other agents to trust. It replays earlier
    /// checkpoints, penalizing each inner agent's weight when its verdict
    /// misaligned with the forward return, then casts a trust-weighted vote.
    /// Relies on the engine's stances being causal (stance[i] depends only on
    /// bars up to i), which holds for every shipped indicator.
    /// </summary>
    public sealed class HedgeMetaAgent(
        int horizon = 10, int step = 5, int warmup = 60, double learningRate = 0.5) : ITradingAgent
    {
        public string Key => "hedge";
        public string DisplayName => "Hedge Meta";

        public AgentReport Analyze(AgentInput input)
        {
            ITradingAgent[] inner =
            [
                new TrendFollowerAgent(),
                new MeanReverterAgent(),
                new ConsensusAgent(),
                new ScorekeeperAgent(horizon),
            ];
            var candles = input.Candles;
            int n = candles.Count;
            var weights = inner.Select(_ => 1.0).ToArray();
            var hits = new int[inner.Length];
            var evaluated = new int[inner.Length];
            int checkpoints = 0;

            for (int t = warmup; t + horizon < n; t += step)
            {
                var truncated = Truncate(input, t);
                checkpoints++;
                double forward = (candles[t + horizon].Close - candles[t].Close) / candles[t].Close;
                for (int a = 0; a < inner.Length; a++)
                {
                    var direction = inner[a].Analyze(truncated).Signal.Direction;
                    double loss;
                    if (direction == SignalDirection.Neutral) loss = 0.5;
                    else
                    {
                        bool ok = direction == SignalDirection.Bullish ? forward > 0 : forward < 0;
                        evaluated[a]++;
                        if (ok) hits[a]++;
                        loss = ok ? 0 : 1;
                    }
                    weights[a] *= Math.Exp(-learningRate * loss);
                }
                double sum = weights.Sum();
                for (int a = 0; a < inner.Length; a++) weights[a] /= sum;
            }

            // Current verdicts on the full window, combined by learned trust.
            double bullWeight = 0, bearWeight = 0;
            var voteDetails = new List<string>();
            var trustDetails = new List<string>();
            for (int a = 0; a < inner.Length; a++)
            {
                var report = inner[a].Analyze(input);
                var direction = report.Signal.Direction;
                if (direction == SignalDirection.Bullish) bullWeight += weights[a];
                else if (direction == SignalDirection.Bearish) bearWeight += weights[a];
                voteDetails.Add($"{inner[a].Key} votes {direction} ({report.Signal.Confidence:0}%)");
                trustDetails.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{inner[a].Key}: weight {weights[a]:0.00}, aligned {hits[a]}/{evaluated[a]} directional calls"));
            }

            TradeSignal signal;
            double totalDirectional = bullWeight + bearWeight;
            if (checkpoints == 0)
                signal = new TradeSignal(SignalDirection.Neutral, 0,
                    $"Not enough history to learn trust (need more than {warmup + horizon} bars).");
            else if (totalDirectional == 0)
                signal = new TradeSignal(SignalDirection.Neutral, 0,
                    $"Trust learned over {checkpoints} checkpoints, but no inner agent is directional right now.");
            else
            {
                var direction = bullWeight > bearWeight ? SignalDirection.Bullish
                    : bearWeight > bullWeight ? SignalDirection.Bearish
                    : SignalDirection.Neutral;
                double confidence = Math.Round(100 * Math.Abs(bullWeight - bearWeight) / totalDirectional);
                int best = Array.IndexOf(weights, weights.Max());
                signal = new TradeSignal(direction, confidence, string.Create(CultureInfo.InvariantCulture,
                    $"Trust-weighted vote over {checkpoints} checkpoints: bullish {bullWeight:0.00} vs bearish {bearWeight:0.00}; most trusted agent is {inner[best].Key} ({weights[best]:0.00})."));
            }

            return new AgentReport(Key, DisplayName, input.Ticker, signal,
            [
                new ScoreCard("Agent trust (learned online)", checkpoints, checkpoints, trustDetails),
                new ScoreCard("Current votes", bullWeight - bearWeight, inner.Length, voteDetails),
            ]);
        }

        /// <summary>Rebuild the input as it looked at bar t (stances/columns are causal, so slicing is faithful).</summary>
        private static AgentInput Truncate(AgentInput input, int t)
        {
            var candles = input.Candles.Take(t + 1).ToList();
            var cutoff = candles[^1].Timestamp;
            var signals = new EngineResult(
                input.Signals.Columns.Select(c => (c.Name, c.Values[..(t + 1)])).ToList(),
                input.Signals.Triggers.Where(tr => tr.Timestamp <= cutoff).ToList(),
                input.Signals.Stances.ToDictionary(s => s.Key, s => s.Value[..(t + 1)]));
            return new AgentInput(input.Ticker, input.Interval, candles, signals);
        }
    }
}
