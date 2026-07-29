using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Agents {
    // The shipped agent pack, following ai-hedge-fund-net's two-phase pattern:
    // deterministic rule-based scoring first, narrative second. Each agent reads
    // the indicator engine's stances (matched by name prefix, so it degrades
    // gracefully when an indicator pack isn't loaded) and emits one TradeSignal.
    // The reasoning text is generated from the scorecards in code; swapping in an
    // LLM narrator later only means replacing BuildSignal's reasoning string.

    internal static class AgentScoring {
        /// <summary>Tally the current stance of every indicator whose name starts with one of the prefixes.</summary>
        public static (ScoreCard Card, int Bull, int Bear, int Total) Tally(
            string title, AgentInput input, params string[] prefixes) {
            var details = new List<string>();
            int bull = 0, bear = 0, total = 0;
            foreach (var (name, stance) in input.Signals.Stances.OrderBy(s => s.Key))
            {
                if (!prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal))) continue;
                total++;
                var (direction, since) = Stances.LatestRun(stance);
                if (direction == SignalDirection.Bullish) bull++;
                else if (direction == SignalDirection.Bearish) bear++;
                details.Add(direction == SignalDirection.Neutral
                    ? $"{name} is Neutral"
                    : $"{name} is {direction} since {input.Candles[since].Timestamp:yyyy-MM-dd}");
            }
            return (new ScoreCard(title, bull - bear, total, details), bull, bear, total);
        }

        public static TradeSignal BuildSignal(int bull, int bear, int total, string style) {
            if (total == 0)
                return new TradeSignal(SignalDirection.Neutral, 0,
                    $"No {style} indicators are loaded, so there is nothing to score.");

            int net = bull - bear;
            var direction = net > 0 ? SignalDirection.Bullish
                : net < 0 ? SignalDirection.Bearish
                : SignalDirection.Neutral;
            double confidence = Math.Round(100.0 * Math.Abs(net) / total);
            return new TradeSignal(direction, confidence,
                $"{bull} bullish vs {bear} bearish of {total} {style} indicators (net {net:+0;-0;0}).");
        }
    }
}