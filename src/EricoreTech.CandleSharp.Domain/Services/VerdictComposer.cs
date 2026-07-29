using System.Globalization;

namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// Rolls every agent's report into one plain-English bottom line. The
    /// vote is confidence-weighted across directional agents; a clear margin
    /// is required before the verdict leans either way, so disagreement
    /// honestly reads as "no clear edge" instead of a coin flip.
    /// </summary>
    public static class VerdictComposer
    {
        private const double LeanThreshold = 0.25;

        public static Verdict Compose(AgentInput input, IReadOnlyList<AgentReport> reports)
        {
            var candles = input.Candles;
            var opinions = reports.Where(r => r.Key != "risk_manager").ToList();
            var directional = opinions.Where(r => r.Signal.Direction != SignalDirection.Neutral).ToList();
            double bullWeight = directional
                .Where(r => r.Signal.Direction == SignalDirection.Bullish)
                .Sum(r => r.Signal.Confidence);
            double bearWeight = directional
                .Where(r => r.Signal.Direction == SignalDirection.Bearish)
                .Sum(r => r.Signal.Confidence);
            double total = bullWeight + bearWeight;
            double net = total == 0 ? 0 : (bullWeight - bearWeight) / total;

            SignalDirection direction;
            string action;
            if (net > LeanThreshold) { direction = SignalDirection.Bullish; action = "LEANING BUY"; }
            else if (net < -LeanThreshold) { direction = SignalDirection.Bearish; action = "LEANING SELL / AVOID"; }
            else { direction = SignalDirection.Neutral; action = "NO CLEAR EDGE — HOLD / WAIT"; }
            double confidence = Math.Round(Math.Abs(net) * 100);

            var details = new List<string>();
            if (candles.Count > 1)
            {
                var last = candles[^1];
                int lookback = Math.Min(21, candles.Count - 1);
                double recent = last.Close / candles[^(lookback + 1)].Close - 1;
                double full = last.Close / candles[0].Close - 1;
                details.Add(string.Create(CultureInfo.InvariantCulture,
                    $"Price {last.Close:0.00} as of {last.Timestamp:yyyy-MM-dd}: {recent:+0.0%;-0.0%} over the last {lookback} bars, {full:+0.0%;-0.0%} over the loaded history."));
            }

            int bullCount = directional.Count(r => r.Signal.Direction == SignalDirection.Bullish);
            int bearCount = directional.Count - bullCount;
            details.Add(string.Create(CultureInfo.InvariantCulture,
                $"Agents: {bullCount} bullish vs {bearCount} bearish out of {opinions.Count} with an opinion (confidence-weighted {bullWeight:0} vs {bearWeight:0})."));

            foreach (var report in directional.OrderByDescending(r => r.Signal.Confidence).Take(2))
                details.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{report.DisplayName} ({report.Signal.Direction} {report.Signal.Confidence:0}%): {report.Signal.Reasoning}"));

            if (reports.FirstOrDefault(r => r.Key == "regime") is { } regime)
                details.Add($"Context — {regime.Signal.Reasoning}");

            if (reports.FirstOrDefault(r => r.Key == "risk_manager") is { } risk)
                details.Add($"If you do trade it — {risk.Signal.Reasoning}");

            var recentTriggers = input.Signals.Triggers.TakeLast(3).ToList();
            if (recentTriggers.Count > 0)
                details.Add("Latest signals: " + string.Join("; ", recentTriggers.Select(t =>
                    $"{t.Indicator} fired {t.Direction} on {t.Timestamp:yyyy-MM-dd}")) + ".");

            details.Add("Automated technical read of price history only — not financial advice.");

            string headline = direction == SignalDirection.Neutral
                ? $"{input.Ticker}: {action}"
                : string.Create(CultureInfo.InvariantCulture, $"{input.Ticker}: {action} ({confidence:0}% conviction)");
            return new Verdict(direction, confidence, headline, details);
        }
    }
}
