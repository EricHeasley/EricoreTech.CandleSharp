using System.Globalization;

namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// Proposes buy/sell rules from a ticker's past performance. It writes
    /// candidate conditions in <see cref="RuleCondition"/> syntax — every
    /// indicator stance and flip, low/high values of every indicator (cut at the
    /// discovery period's 10th/20th/80th/90th percentiles), and the close
    /// crossing every price-level line — then scores each as a Buy and as a
    /// Sell by what the stock did after it fired. The strongest singles are also
    /// tried in pairs, kept only when the pair beats both halves.
    ///
    /// Everything is chosen on the older part of history. The recent
    /// TestFraction is used only afterwards, to check each winner; rules that
    /// clearly failed that check are dropped. Because suggestions are real rule
    /// text evaluated by the same code as saved rules, a saved suggestion
    /// behaves exactly as it was scored.
    /// </summary>
    public static class RuleSuggester
    {
        private static readonly double[] LowCuts = [0.10, 0.20];
        private static readonly double[] HighCuts = [0.80, 0.90];
        private const int PairPool = 20;
        private const int MinRecentSignals = 3;

        // A recent-period t-score this high is unlikely to be chance for a single, pre-chosen rule.
        private const double RecentConfirmT = 2.0;

        public static SuggestionReport Suggest(
            string ticker, RuleData data, SuggestOptions? options = null, IReadOnlySet<string>? exclude = null)
        {
            var o = options ?? new SuggestOptions();
            o.Validate();
            exclude ??= new HashSet<string>();

            int n = data.Count;
            int h = o.Horizon;
            var close = data.Values("Close");
            var forward = new double?[n];
            for (int i = 0; i + h < n; i++)
                if (close[i] is > 0 && close[i + h] is { } later)
                    forward[i] = later / close[i]!.Value - 1;
            int eligible = Math.Max(0, n - h);
            int trainEnd = eligible - (int)Math.Round(eligible * o.TestFraction);
            if (trainEnd < 50)
                throw new InvalidOperationException(
                    $"not enough history to suggest rules: need at least {50 + h + (int)Math.Ceiling(50 * o.TestFraction / (1 - o.TestFraction))} bars, have {n}");

            var train = new Period(0, trainEnd, Mean(forward, 0, trainEnd));
            var recent = new Period(trainEnd, eligible, Mean(forward, trainEnd, eligible));

            var singles = new List<Candidate>();
            foreach (var conditions in CandidateConditions(data, trainEnd, exclude))
                if (Mask(data, conditions) is { } mask)
                    singles.Add(new Candidate(conditions, mask));

            int tested = 0;
            var buy = new List<Scored>();
            var sell = new List<Scored>();
            foreach (var action in new[] { RuleAction.Buy, RuleAction.Sell })
            {
                var kept = action == RuleAction.Buy ? buy : sell;
                foreach (var candidate in singles)
                {
                    tested++;
                    if (Stats(candidate.Mask, forward, action, train, h) is { } s && Qualifies(s, o))
                        kept.Add(new Scored(candidate, s));
                }
                if (!o.IncludePairs) continue;

                var pool = kept.OrderByDescending(k => k.Train.TScore).Take(PairPool).ToList();
                for (int a = 0; a < pool.Count; a++)
                    for (int b = a + 1; b < pool.Count; b++)
                    {
                        // Two cuts of the same indicator ("RSI < 30" + "RSI < 40") add nothing.
                        if (pool[a].Candidate.Subject == pool[b].Candidate.Subject) continue;
                        tested++;
                        var mask = new bool[n];
                        for (int i = 0; i < n; i++) mask[i] = pool[a].Candidate.Mask[i] && pool[b].Candidate.Mask[i];
                        var pair = new Candidate([.. pool[a].Candidate.Conditions, .. pool[b].Candidate.Conditions], mask);
                        if (Stats(mask, forward, action, train, h) is { } s && Qualifies(s, o)
                            && s.TScore > Math.Max(pool[a].Train.TScore, pool[b].Train.TScore))
                            kept.Add(new Scored(pair, s));
                    }
            }

            // The largest of N independent noise t-scores is about sqrt(2 ln N).
            double luckBar = Math.Sqrt(2 * Math.Log(Math.Max(tested, 2)));
            return new SuggestionReport(
                ticker.ToUpperInvariant(), o, data.Candles[trainEnd - 1].Timestamp, tested, luckBar,
                Pick(buy, RuleAction.Buy, ticker, forward, recent, o, luckBar),
                Pick(sell, RuleAction.Sell, ticker, forward, recent, o, luckBar));
        }

        private sealed record Period(int From, int To, double Baseline);

        /// <summary>Subject = the first name a candidate reads, used to avoid pairing an indicator with itself.</summary>
        private sealed record Candidate(IReadOnlyList<string> Conditions, bool[] Mask)
        {
            public string Subject { get; } = RuleCondition.Parse(Conditions[0]).ReferencedNames
                .FirstOrDefault(name => !name.Equals("Close", StringComparison.OrdinalIgnoreCase))
                ?? Conditions[0];
        }

        private sealed record Scored(Candidate Candidate, SignalStats Train);

        private static bool Qualifies(SignalStats s, SuggestOptions o) => s.Signals >= o.MinSignals && s.Edge > 0;

        private static IReadOnlyList<RuleSuggestion> Pick(
            List<Scored> scored, RuleAction action, string ticker, double?[] forward, Period recent, SuggestOptions o,
            double luckBar)
        {
            var picked = new List<RuleSuggestion>();
            var seen = new HashSet<string>();
            foreach (var s in scored.OrderByDescending(s => s.Train.TScore))
            {
                // Different wording, same signals (e.g. two cut points that round alike): keep the first.
                if (!seen.Add(Signature(s.Candidate.Mask))) continue;
                var test = Stats(s.Candidate.Mask, forward, action, recent, o.Horizon);
                bool? held = test is { Signals: >= MinRecentSignals } t ? t.Edge > 0 : null;
                if (held == false) continue;
                var rule = new TradeRule(
                    "", string.Join(" and ", s.Candidate.Conditions), ticker.ToUpperInvariant(), action,
                    s.Candidate.Conditions, o.Horizon);
                bool beatsLuck = s.Train.TScore >= luckBar;
                bool confirmed = held == true && test!.TScore >= RecentConfirmT;
                var strength = beatsLuck && confirmed ? SuggestionStrength.Strong
                    : beatsLuck || confirmed ? SuggestionStrength.Promising
                    : SuggestionStrength.Weak;
                picked.Add(new RuleSuggestion(rule, s.Train, test, held, s.Candidate.Mask is [.., true], strength));
            }
            // Strongest first; within a grade, confirmed on recent data before too-rare-to-judge.
            return picked.OrderByDescending(p => p.Strength).ThenByDescending(p => p.HeldUp == true)
                .ThenByDescending(p => p.Discovery.TScore)
                .Take(o.Top).ToList();
        }

        private static IEnumerable<IReadOnlyList<string>> CandidateConditions(RuleData data, int trainEnd, IReadOnlySet<string> exclude)
        {
            foreach (var name in data.StanceNames.Where(s => !exclude.Contains(s)).Order(StringComparer.Ordinal))
                foreach (var verb in new[] { "is", "turns" })
                    foreach (var direction in new[] { "Bullish", "Bearish" })
                        yield return [$"{name} {verb} {direction}"];

            var close = data.Values("Close");
            string[] prices = ["Open", "High", "Low", "Close"];
            foreach (var name in data.ValueNames)
            {
                if (exclude.Contains(name) || prices.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                var raw = data.Values(name);
                bool relative = IsPriceLevel(raw, close);
                var values = new List<double>();
                for (int i = 0; i < trainEnd; i++)
                    if (raw[i] is { } v && close[i] is > 0 && double.IsFinite(v))
                        values.Add(relative ? v / close[i]!.Value - 1 : v);
                if (values.Distinct().Count() < 20) continue;
                values.Sort();

                string operand = relative ? $"{name} vs close" : name;
                string Cut(double q) => Format(values[(int)(q * (values.Count - 1))], relative);
                foreach (var q in LowCuts) yield return [$"{operand} < {Cut(q)}"];
                foreach (var q in HighCuts) yield return [$"{operand} > {Cut(q)}"];
                if (relative)
                {
                    yield return [$"Close crosses above {name}"];
                    yield return [$"Close crosses below {name}"];
                }
            }
        }

        private static bool[]? Mask(RuleData data, IReadOnlyList<string> conditions)
        {
            try
            {
                var mask = Enumerable.Repeat(true, data.Count).ToArray();
                foreach (var text in conditions)
                {
                    var holds = RuleCondition.Parse(text).Evaluate(data);
                    for (int i = 0; i < mask.Length; i++) mask[i] &= holds[i];
                }
                return mask.Any(b => b) ? mask : null;
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Scores the signals (first bar of each run where the mask holds) that start
        /// inside the period, in the action's direction. Null when there are none.
        /// </summary>
        private static SignalStats? Stats(bool[] mask, double?[] forward, RuleAction action, Period period, int horizon)
        {
            double sign = action == RuleAction.Buy ? 1 : -1;
            var raw = new List<double>();
            var aligned = new List<double>();
            int independent = 0, lastCounted = -horizon;
            for (int i = period.From; i < period.To; i++)
            {
                if (!mask[i] || (i > 0 && mask[i - 1]) || forward[i] is not { } r) continue;
                raw.Add(r);
                aligned.Add(sign * (r - period.Baseline));
                if (i - lastCounted >= horizon)
                {
                    independent++;
                    lastCounted = i;
                }
            }
            if (raw.Count == 0) return null;

            double edge = aligned.Average();
            double t = 0;
            if (aligned.Count > 1 && independent > 1)
            {
                double sd = Math.Sqrt(aligned.Sum(a => (a - edge) * (a - edge)) / (aligned.Count - 1));
                if (sd > 0) t = edge / (sd / Math.Sqrt(independent));
            }
            return new SignalStats(raw.Count, raw.Average(), raw.Count(r => sign * r > 0) / (double)raw.Count, edge, t);
        }

        private static bool IsPriceLevel(double?[] values, double?[] close)
        {
            int total = 0, near = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] is not { } v || close[i] is not > 0) continue;
                total++;
                if (v / close[i]!.Value is >= 0.5 and <= 2) near++;
            }
            return total > 0 && near >= 0.9 * total;
        }

        private static string Format(double v, bool relative) => relative
            ? (v * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%"
            : v.ToString("G4", CultureInfo.InvariantCulture);

        private static double Mean(double?[] values, int from, int to)
        {
            double sum = 0;
            int count = 0;
            for (int i = from; i < to; i++)
                if (values[i] is { } v)
                {
                    sum += v;
                    count++;
                }
            return count > 0 ? sum / count : 0;
        }

        private static string Signature(bool[] mask) =>
            string.Join(',', Enumerable.Range(0, mask.Length).Where(i => mask[i] && (i == 0 || !mask[i - 1])));
    }
}
