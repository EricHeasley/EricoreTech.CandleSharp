using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>
    /// Use cases for user-defined buy/sell rules: save (validated against real
    /// data so typos and future-peeking indicators are caught up front), delete,
    /// and check rules against stored history — is it firing now, and how did
    /// it do every time it fired before?
    /// </summary>
    public sealed class RuleService(ICandleRepository repository, IRuleRepository rules, IPluginCatalog catalog)
    {
        public IReadOnlyList<TradeRule> List() => rules.LoadRules();

        public TradeRule Add(
            string? name, string ticker, RuleAction action, IReadOnlyList<string> conditions,
            int horizon = 10, string interval = "1d")
        {
            ticker = string.IsNullOrWhiteSpace(ticker) ? TradeRule.AnyTicker : ticker.Trim().ToUpperInvariant();
            var cleaned = conditions.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            if (cleaned.Count == 0)
                throw new ArgumentException("a rule needs at least one condition");
            if (horizon < 1)
                throw new ArgumentOutOfRangeException(nameof(horizon), "horizon must be at least 1 bar");

            // Parsing normalizes the text ("RSI_14<30" -> "RSI_14 < 30") and reports syntax errors.
            var parsed = cleaned.Select(RuleCondition.Parse).ToList();
            Validate(parsed, ticker, interval);

            var rule = new TradeRule(
                Guid.NewGuid().ToString("N")[..8],
                string.IsNullOrWhiteSpace(name) ? string.Join(" and ", parsed.Select(p => p.Text)) : name.Trim(),
                ticker,
                action,
                parsed.Select(p => p.Text).ToList(),
                horizon);
            var all = rules.LoadRules();
            all.Add(rule);
            rules.SaveRules(all);
            return rule;
        }

        public bool Remove(string id)
        {
            var all = rules.LoadRules();
            int removed = all.RemoveAll(r => r.Id == id);
            if (removed > 0) rules.SaveRules(all);
            return removed > 0;
        }

        /// <summary>Every rule that applies to the ticker, checked against its stored history.</summary>
        public IReadOnlyList<RuleResult> Evaluate(string ticker, string interval)
        {
            var applicable = rules.LoadRules().Where(r => r.AppliesTo(ticker)).ToList();
            if (applicable.Count == 0) return [];
            var (data, leaky) = Load(ticker, interval);
            return applicable.Select(r => Run(r, ticker, data, leaky)).ToList();
        }

        /// <summary>Every rule against every saved dataset of the interval it applies to.</summary>
        public IReadOnlyList<RuleResult> EvaluateAll(string interval)
        {
            var all = rules.LoadRules();
            var results = new List<RuleResult>();
            foreach (var dataset in repository.List().Where(d => d.Interval == interval))
            {
                var applicable = all.Where(r => r.AppliesTo(dataset.Ticker)).ToList();
                if (applicable.Count == 0) continue;
                var (data, leaky) = Load(dataset.Ticker, interval);
                results.AddRange(applicable.Select(r => Run(r, dataset.Ticker, data, leaky)));
            }
            return results;
        }

        /// <summary>The names a rule can use for this dataset, for building rules.</summary>
        public RuleOperands Operands(string ticker, string interval)
        {
            var (data, leaky) = Load(ticker, interval);
            return new RuleOperands(
                data.ValueNames.Where(n => !leaky.Contains(n)).ToList(),
                data.StanceNames.Where(n => !leaky.Contains(n)).Order(StringComparer.Ordinal).ToList());
        }

        private (RuleData Data, HashSet<string> Leaky) Load(string ticker, string interval)
        {
            var candles = repository.Load(ticker, interval);
            var engine = catalog.CreateEngine();
            var (columns, stances) = LookaheadDetector.Find(engine, candles);
            var leaky = new HashSet<string>(columns.Concat(stances), StringComparer.OrdinalIgnoreCase);
            return (new RuleData(candles, engine.Run(candles)), leaky);
        }

        private static RuleResult Run(TradeRule rule, string ticker, RuleData data, HashSet<string> leaky)
        {
            try
            {
                var parsed = rule.Conditions.Select(RuleCondition.Parse).ToList();
                if (FirstProblem(parsed, data, leaky) is { } problem)
                    return RuleResult.Failed(rule, ticker, problem);
                return RuleEvaluator.Evaluate(rule, ticker, data);
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException)
            {
                return RuleResult.Failed(rule, ticker, ex.Message);
            }
        }

        /// <summary>Checks names against real data: the rule's own ticker if stored, else any saved dataset.</summary>
        private void Validate(List<RuleCondition> parsed, string ticker, string interval)
        {
            var datasets = repository.List().Where(d => d.Interval == interval).ToList();
            var sample = datasets.FirstOrDefault(d => d.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase))
                ?? datasets.FirstOrDefault();
            if (sample is null) return; // nothing stored yet to check names against
            var (data, leaky) = Load(sample.Ticker, interval);
            if (FirstProblem(parsed, data, leaky) is { } problem)
                throw new ArgumentException(problem);
        }

        private static string? FirstProblem(List<RuleCondition> parsed, RuleData data, HashSet<string> leaky)
        {
            foreach (var condition in parsed)
                foreach (var name in condition.ReferencedNames)
                {
                    if (leaky.Contains(name))
                        return $"\"{condition.Text}\": {name} uses future prices (it's drawn shifted back on charts), so it can't be used in a rule";
                    bool known = condition.IsStanceCondition ? data.HasStance(name) : data.HasValue(name);
                    if (!known)
                        return condition.IsStanceCondition
                            ? $"\"{condition.Text}\": no indicator stance named {name}"
                            : $"\"{condition.Text}\": no value named {name}";
                }
            return null;
        }
    }
}
