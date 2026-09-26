using System.Globalization;

namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// Mines stored indicator snapshots for conditions that preceded unusual
    /// performance. For every bar it measures the forward return over the next
    /// Horizon bars, then asks of each condition — an indicator's stance, a fresh
    /// stance flip, an indicator value falling in a bucket, or two stances at
    /// once — "when this was true, how did the stock do compared with usual?"
    ///
    /// Discovery runs on the older part of each ticker's history; the recent
    /// TestFraction is held out and used only to check whether a pattern's edge
    /// kept its sign. Buckets are cut from in-sample values only, so nothing
    /// about the held-out period leaks into how conditions are defined.
    /// </summary>
    public static class PatternAnalyzer
    {
        private const double NotableT = 2.0;

        // Two-sided P(|Z| >= 2): the share of pure-noise conditions expected to look notable.
        private const double ChanceRate = 0.0455;

        /// <param name="excludeColumns">Columns to leave out, e.g. ones that use future bars.</param>
        /// <param name="excludeStances">Indicator stances to leave out, likewise.</param>
        public static PatternReport Analyze(
            IReadOnlyList<SnapshotTable> tables,
            PatternOptions? options = null,
            IReadOnlySet<string>? excludeColumns = null,
            IReadOnlySet<string>? excludeStances = null)
        {
            var o = options ?? new PatternOptions();
            o.Validate();
            if (tables.Count == 0)
                throw new ArgumentException("no snapshot tables to analyze", nameof(tables));

            var excluded = new List<string>();
            if (excludeColumns is { Count: > 0 } || excludeStances is { Count: > 0 })
            {
                excluded.AddRange((excludeColumns ?? new HashSet<string>()).Order(StringComparer.Ordinal));
                excluded.AddRange((excludeStances ?? new HashSet<string>()).Order(StringComparer.Ordinal).Select(s => $"stance:{s}"));
                tables = tables.Select(t => t with
                {
                    Columns = t.Columns.Where(c => excludeColumns?.Contains(c.Name) != true).ToList(),
                    Stances = t.Stances.Where(s => excludeStances?.Contains(s.Key) != true)
                        .ToDictionary(s => s.Key, s => s.Value),
                }).ToList();
            }

            var samples = tables.Select(t => new Sample(t, o)).ToList();
            if (samples.Sum(s => s.TrainEnd) == 0)
                throw new InvalidOperationException(
                    $"not enough history: need more than {o.Horizon} bars to measure {o.Horizon}-bar forward returns");

            var baseline = Baseline(samples, s => (0, s.TrainEnd));
            var oosBaseline = Baseline(samples, s => (s.TrainEnd, s.Eligible));
            var ctx = new Context(samples, o, baseline, oosBaseline);

            var stanceConditions = StanceConditions(samples);
            var triggerConditions = TriggerConditions(samples);
            var studies = new List<ColumnStudy>();
            var bucketStats = new List<PatternStat>();
            foreach (var study in ColumnStudies(ctx))
            {
                studies.Add(study);
                bucketStats.AddRange(study.Buckets);
            }

            var stats = new List<PatternStat>();
            stats.AddRange(stanceConditions.Select(c => Evaluate(ctx, c)));
            stats.AddRange(triggerConditions.Select(c => Evaluate(ctx, c)));
            stats.AddRange(bucketStats);
            if (o.IncludeCombos)
                stats.AddRange(Combos(stanceConditions).Select(c => Evaluate(ctx, c)));

            var qualifying = stats.Where(s => s.Samples >= o.MinSamples).ToList();
            var notable = qualifying.Where(s => Math.Abs(s.TScore) >= NotableT).ToList();

            return new PatternReport(
                tables.Select(t => t.Ticker).ToList(),
                o,
                samples.Sum(s => s.Eligible),
                baseline,
                oosBaseline,
                qualifying.Count,
                notable.Count,
                notable.Count(s => s.HoldsOutOfSample is not null),
                notable.Count(s => s.HoldsOutOfSample == true),
                qualifying.Count * ChanceRate,
                qualifying.Where(s => s.Edge > 0).OrderByDescending(s => s.TScore).Take(o.Top).ToList(),
                qualifying.Where(s => s.Edge < 0).OrderBy(s => s.TScore).Take(o.Top).ToList(),
                qualifying.Where(s => s.ActiveIn.Count > 0)
                    .OrderByDescending(s => Math.Abs(s.TScore)).Take(o.Top).ToList(),
                studies,
                excluded);
        }

        /// <summary>One ticker's history with forward returns and its train/test split.</summary>
        private sealed class Sample
        {
            public Sample(SnapshotTable table, PatternOptions o)
            {
                Table = table;
                int n = table.Count;
                Forward = new double?[n];
                for (int i = 0; i + o.Horizon < n; i++)
                    if (table.Closes[i] > 0)
                        Forward[i] = table.Closes[i + o.Horizon] / table.Closes[i] - 1;
                Eligible = Math.Max(0, n - o.Horizon);
                TrainEnd = Eligible - (int)Math.Round(Eligible * o.TestFraction);
            }

            public SnapshotTable Table { get; }

            /// <summary>Close-to-close return Horizon bars ahead; null for the last Horizon bars.</summary>
            public double?[] Forward { get; }

            /// <summary>Bars [0, Eligible) have a forward return.</summary>
            public int Eligible { get; }

            /// <summary>Bars [0, TrainEnd) are in-sample; [TrainEnd, Eligible) are held out.</summary>
            public int TrainEnd { get; }
        }

        private sealed record Context(
            List<Sample> Samples, PatternOptions Options, BaselineStat Baseline, BaselineStat OosBaseline);

        /// <summary>
        /// A named condition: one bar mask per sample. Source is the indicator it came
        /// from; Rule is the equivalent rule-condition text (null if not expressible).
        /// </summary>
        private sealed record Condition(
            string Label, string Kind, string Source, bool[][] Masks, IReadOnlyList<string>? Rule);

        private static BaselineStat Baseline(List<Sample> samples, Func<Sample, (int From, int To)> range)
        {
            var returns = new List<double>();
            foreach (var s in samples)
            {
                var (from, to) = range(s);
                for (int i = from; i < to; i++)
                    if (s.Forward[i] is { } r) returns.Add(r);
            }
            return returns.Count == 0
                ? new BaselineStat(0, 0, 0)
                : new BaselineStat(returns.Count, returns.Average(), returns.Count(r => r > 0) / (double)returns.Count);
        }

        private static List<Condition> StanceConditions(List<Sample> samples)
        {
            var conditions = new List<Condition>();
            foreach (var name in StanceNames(samples))
                foreach (var direction in new[] { SignalDirection.Bullish, SignalDirection.Bearish })
                {
                    var masks = samples.Select(s => s.Table.Stances.TryGetValue(name, out var stance)
                        ? stance.Select(d => d == direction).ToArray()
                        : new bool[s.Table.Count]).ToArray();
                    if (masks.Any(m => m.Any(b => b)))
                        conditions.Add(new Condition($"{name} is {direction}", "stance", name, masks, [$"{name} is {direction}"]));
                }
            return conditions;
        }

        private static List<Condition> TriggerConditions(List<Sample> samples)
        {
            var conditions = new List<Condition>();
            foreach (var name in StanceNames(samples))
                foreach (var direction in new[] { SignalDirection.Bullish, SignalDirection.Bearish })
                {
                    var masks = samples.Select(s =>
                    {
                        var mask = new bool[s.Table.Count];
                        if (s.Table.Stances.TryGetValue(name, out var stance))
                            // Bar 0 has no known previous stance, so it can't be a flip.
                            for (int i = 1; i < stance.Length; i++)
                                mask[i] = stance[i] == direction && stance[i - 1] != direction;
                        return mask;
                    }).ToArray();
                    if (masks.Any(m => m.Any(b => b)))
                        conditions.Add(new Condition($"{name} turns {direction}", "trigger", name, masks, [$"{name} turns {direction}"]));
                }
            return conditions;
        }

        private static IEnumerable<string> StanceNames(List<Sample> samples) =>
            samples.SelectMany(s => s.Table.Stances.Keys).Distinct().Order(StringComparer.Ordinal);

        /// <summary>Every pair of directional stances from two different indicators.</summary>
        private static IEnumerable<Condition> Combos(List<Condition> stances)
        {
            for (int a = 0; a < stances.Count; a++)
                for (int b = a + 1; b < stances.Count; b++)
                {
                    var x = stances[a];
                    var y = stances[b];
                    if (x.Source == y.Source) continue;
                    var masks = new bool[x.Masks.Length][];
                    for (int s = 0; s < masks.Length; s++)
                    {
                        masks[s] = new bool[x.Masks[s].Length];
                        for (int i = 0; i < masks[s].Length; i++)
                            masks[s][i] = x.Masks[s][i] && y.Masks[s][i];
                    }
                    yield return new Condition($"{x.Label} + {y.Label}", "combo", $"{x.Source}+{y.Source}", masks,
                        [.. x.Rule!, .. y.Rule!]);
                }
        }

        private static IEnumerable<ColumnStudy> ColumnStudies(Context ctx)
        {
            var samples = ctx.Samples;
            int bucketCount = ctx.Options.Buckets;
            var names = samples.SelectMany(s => s.Table.Columns.Select(c => c.Name)).Distinct().ToList();

            foreach (var name in names)
            {
                var raw = samples.Select(s => (double?[]?)s.Table.Columns.FirstOrDefault(c => c.Name == name).Values).ToList();
                bool relative = IsPriceLevel(samples, raw);
                var features = samples.Select((s, k) => raw[k] is null
                    ? new double?[s.Table.Count]
                    : raw[k]!.Select((v, i) => relative
                        ? v is { } x && s.Table.Closes[i] > 0 ? x / s.Table.Closes[i] - 1 : (double?)null
                        : v).ToArray()).ToList();

                // Each ticker gets its own cut points from its own in-sample values, so
                // pooling tickers with different price scales still compares like with like.
                var cutoffs = samples.Select((s, k) => Cutoffs(features[k], s.TrainEnd, bucketCount)).ToList();
                if (cutoffs.All(c => c is null)) continue;

                string display = relative ? $"{name} vs close" : name;
                var buckets = new List<PatternStat>();
                for (int b = 0; b < bucketCount; b++)
                {
                    var masks = samples.Select((s, k) =>
                    {
                        var mask = new bool[s.Table.Count];
                        if (cutoffs[k] is { } cuts)
                            for (int i = 0; i < mask.Length; i++)
                                mask[i] = features[k][i] is { } v && BucketOf(v, cuts) == b;
                        return mask;
                    }).ToArray();

                    var label = $"{display} in {BucketName(b, bucketCount)}";
                    IReadOnlyList<string>? rule = null;
                    if (samples.Count == 1 && cutoffs[0] is { } only)
                    {
                        label += $" ({BucketRange(b, only, relative)})";
                        rule = BucketRule(b, only, relative ? $"{name} vs close" : name, relative);
                    }
                    buckets.Add(Evaluate(ctx, new Condition(label, "bucket", name, masks, rule)));
                }

                yield return new ColumnStudy(
                    name, relative,
                    Correlation(samples, features, s => (0, s.TrainEnd), ctx.Options.MinSamples),
                    Correlation(samples, features, s => (s.TrainEnd, s.Eligible), ctx.Options.MinSamples),
                    buckets);
            }
        }

        /// <summary>
        /// A column is a price level (moving average, band, stop) when nearly all of its
        /// values sit within 0.5x–2x of the close; those are only meaningful relative to price.
        /// </summary>
        private static bool IsPriceLevel(List<Sample> samples, List<double?[]?> raw)
        {
            int total = 0, near = 0;
            for (int k = 0; k < samples.Count; k++)
            {
                if (raw[k] is not { } values) continue;
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i] is not { } v || samples[k].Table.Closes[i] <= 0) continue;
                    total++;
                    double ratio = v / samples[k].Table.Closes[i];
                    if (ratio is >= 0.5 and <= 2) near++;
                }
            }
            return total > 0 && near >= 0.9 * total;
        }

        /// <summary>Quantile cut points of the in-sample values; null when there's too little variety to bucket.</summary>
        private static double[]? Cutoffs(double?[] feature, int trainEnd, int bucketCount)
        {
            var values = new List<double>();
            for (int i = 0; i < trainEnd; i++)
                if (feature[i] is { } v && double.IsFinite(v)) values.Add(v);
            if (values.Distinct().Count() < bucketCount) return null;
            values.Sort();
            return Enumerable.Range(1, bucketCount - 1)
                .Select(j => values[(int)((long)j * values.Count / bucketCount)])
                .ToArray();
        }

        private static int BucketOf(double value, double[] cutoffs)
        {
            int bucket = 0;
            while (bucket < cutoffs.Length && value >= cutoffs[bucket]) bucket++;
            return bucket;
        }

        private static string BucketName(int b, int count)
        {
            int lo = b * 100 / count, hi = (b + 1) * 100 / count;
            return b == 0 ? $"bottom {hi}%"
                : b == count - 1 ? $"top {100 - lo}%"
                : $"{lo}-{hi}% band";
        }

        private static string BucketRange(int b, double[] cutoffs, bool relative)
        {
            string Fmt(double v) => relative
                ? Math.Round(v, 3).ToString("+0.0%;-0.0%;0.0%", CultureInfo.InvariantCulture)
                : v.ToString("G4", CultureInfo.InvariantCulture);
            return b == 0 ? $"< {Fmt(cutoffs[0])}"
                : b == cutoffs.Length ? $">= {Fmt(cutoffs[^1])}"
                : $"{Fmt(cutoffs[b - 1])} to {Fmt(cutoffs[b])}";
        }

        /// <summary>The bucket as rule conditions: bucket b holds cutoffs[b-1] &lt;= v &lt; cutoffs[b].</summary>
        private static IReadOnlyList<string> BucketRule(int b, double[] cutoffs, string operand, bool relative)
        {
            // Rounded like the label, so the rule reads the same as the pattern it came from.
            string Num(double v) => relative
                ? (v * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%"
                : v.ToString("G4", CultureInfo.InvariantCulture);
            var rule = new List<string>();
            if (b > 0) rule.Add($"{operand} >= {Num(cutoffs[b - 1])}");
            if (b < cutoffs.Length) rule.Add($"{operand} < {Num(cutoffs[b])}");
            return rule;
        }

        private static PatternStat Evaluate(Context ctx, Condition condition)
        {
            var o = ctx.Options;
            var train = new List<double>();
            var test = new List<double>();
            int independent = 0;
            var activeIn = new List<string>();

            for (int k = 0; k < ctx.Samples.Count; k++)
            {
                var s = ctx.Samples[k];
                var mask = condition.Masks[k];
                int lastCounted = -o.Horizon;
                for (int i = 0; i < s.Eligible; i++)
                {
                    if (!mask[i] || s.Forward[i] is not { } r) continue;
                    if (i < s.TrainEnd)
                    {
                        train.Add(r);
                        // Consecutive bars share most of their forward window, so they are
                        // not independent evidence; count only non-overlapping windows.
                        if (i - lastCounted >= o.Horizon)
                        {
                            independent++;
                            lastCounted = i;
                        }
                    }
                    else
                    {
                        test.Add(r);
                    }
                }
                if (mask.Length > 0 && mask[^1])
                    activeIn.Add(s.Table.Ticker);
            }

            double avg = train.Count > 0 ? train.Average() : 0;
            double win = train.Count > 0 ? train.Count(r => r > 0) / (double)train.Count : 0;
            double edge = train.Count > 0 ? avg - ctx.Baseline.AvgReturn : 0;
            double t = 0;
            if (train.Count > 1 && independent > 1)
            {
                double sd = Math.Sqrt(train.Sum(r => (r - avg) * (r - avg)) / (train.Count - 1));
                if (sd > 0) t = edge / (sd / Math.Sqrt(independent));
            }

            bool checkable = test.Count >= Math.Max(5, o.MinSamples / 3);
            double? oosAvg = checkable ? test.Average() : null;
            double? oosWin = checkable ? test.Count(r => r > 0) / (double)test.Count : null;
            double? oosEdge = checkable ? oosAvg - ctx.OosBaseline.AvgReturn : null;
            bool? holds = checkable && edge != 0 ? Math.Sign(oosEdge!.Value) == Math.Sign(edge) : null;

            return new PatternStat(
                condition.Label, condition.Kind, train.Count, avg, win, edge, t,
                test.Count, oosAvg, oosWin, oosEdge, holds, activeIn, condition.Rule);
        }

        /// <summary>Spearman rank correlation per ticker, averaged weighted by sample count.</summary>
        private static double? Correlation(
            List<Sample> samples, List<double?[]> features, Func<Sample, (int From, int To)> range, int minSamples)
        {
            double weighted = 0;
            int weight = 0;
            for (int k = 0; k < samples.Count; k++)
            {
                var (from, to) = range(samples[k]);
                var xs = new List<double>();
                var ys = new List<double>();
                for (int i = from; i < to; i++)
                    if (features[k][i] is { } x && double.IsFinite(x) && samples[k].Forward[i] is { } y)
                    {
                        xs.Add(x);
                        ys.Add(y);
                    }
                if (xs.Count < minSamples) continue;
                if (Pearson(Ranks(xs), Ranks(ys)) is not { } rho) continue;
                weighted += rho * xs.Count;
                weight += xs.Count;
            }
            return weight == 0 ? null : weighted / weight;
        }

        private static double[] Ranks(List<double> values)
        {
            var order = Enumerable.Range(0, values.Count).OrderBy(i => values[i]).ToArray();
            var ranks = new double[values.Count];
            for (int start = 0; start < order.Length;)
            {
                int end = start;
                while (end + 1 < order.Length && values[order[end + 1]] == values[order[start]]) end++;
                double rank = (start + end) / 2.0;
                for (int j = start; j <= end; j++) ranks[order[j]] = rank;
                start = end + 1;
            }
            return ranks;
        }

        private static double? Pearson(double[] xs, double[] ys)
        {
            double mx = xs.Average(), my = ys.Average();
            double sxy = 0, sxx = 0, syy = 0;
            for (int i = 0; i < xs.Length; i++)
            {
                sxy += (xs[i] - mx) * (ys[i] - my);
                sxx += (xs[i] - mx) * (xs[i] - mx);
                syy += (ys[i] - my) * (ys[i] - my);
            }
            return sxx > 0 && syy > 0 ? sxy / Math.Sqrt(sxx * syy) : null;
        }
    }
}
