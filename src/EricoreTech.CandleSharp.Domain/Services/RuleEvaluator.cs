namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// Runs a <see cref="TradeRule"/> over a ticker's history. A signal is the
    /// first bar of each run where every condition holds (a rule that stays true
    /// for a week is one signal, not five), and each past signal is scored by
    /// the close-to-close return over the rule's horizon.
    /// </summary>
    public static class RuleEvaluator
    {
        public static RuleResult Evaluate(TradeRule rule, string ticker, RuleData data)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(rule.Horizon, 1);
            if (rule.Conditions.Count == 0)
                throw new ArgumentException("a rule needs at least one condition");

            int n = data.Count;
            var closes = data.Values("Close");
            var active = Enumerable.Repeat(n > 0, n).ToArray();
            foreach (var text in rule.Conditions)
            {
                var holds = RuleCondition.Parse(text).Evaluate(data);
                for (int i = 0; i < n; i++) active[i] &= holds[i];
            }

            double? Forward(int i) =>
                i + rule.Horizon < n && closes[i] is > 0 && closes[i + rule.Horizon] is { } later
                    ? later / closes[i]!.Value - 1
                    : null;

            var signals = new List<RuleSignal>();
            for (int i = 0; i < n; i++)
                if (active[i] && (i == 0 || !active[i - 1]))
                    signals.Add(new RuleSignal(data.Candles[i].Timestamp, data.Candles[i].Close, Forward(i)));

            var all = Enumerable.Range(0, n).Select(Forward).OfType<double>().ToList();
            double baseline = all.Count > 0 ? all.Average() : 0;

            var scored = signals.Select(s => s.ForwardReturn).OfType<double>().ToList();
            // Direction-aligned: for a Sell rule a fall is a win.
            double sign = rule.Action == RuleAction.Buy ? 1 : -1;
            double? avg = scored.Count > 0 ? scored.Average() : null;

            return new RuleResult(
                rule,
                ticker.ToUpperInvariant(),
                n > 0 ? data.Candles[^1].Timestamp : null,
                n > 0 && active[^1],
                signals.Count > 0 && n > 0 && signals[^1].Timestamp == data.Candles[^1].Timestamp,
                active.Count(a => a),
                signals,
                scored.Count,
                avg,
                scored.Count > 0 ? scored.Count(r => sign * r > 0) / (double)scored.Count : null,
                avg is { } a ? sign * (a - baseline) : null,
                scored.Count > 0 ? (sign > 0 ? scored.Max() : scored.Min()) : null,
                scored.Count > 0 ? (sign > 0 ? scored.Min() : scored.Max()) : null,
                baseline);
        }
    }
}
