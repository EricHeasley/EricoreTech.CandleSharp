namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// Trains a <see cref="GradientBoostedTrees"/> model on one ticker's own
    /// stored indicator snapshots to guess whether it will be higher Horizon
    /// bars from now, then honestly grades that guess with a walk-forward
    /// replay — a fresh model trained at each checkpoint on only the data
    /// available by then, scored against what actually happened next, the
    /// same discipline <see cref="Backtester"/> applies to the rule-based
    /// agents. A model that can't beat that check is exactly as untrustworthy
    /// as it looks.
    ///
    /// Every indicator column in the snapshot becomes one input feature,
    /// price-level ones (moving averages, bands, stops) converted to distance
    /// from the close first, exactly as <see cref="PatternAnalyzer"/> does —
    /// so the model reads "20% above the 50-day average" rather than a raw
    /// price the tree has no way to generalize across tickers or time.
    /// </summary>
    public static class MLGuidanceAnalyzer
    {
        private const int MinTrainingRows = 50;
        private const int Seed = 42;

        public static MLGuidanceReport Analyze(
            SnapshotTable table, MLGuidanceOptions? options = null, IReadOnlySet<string>? excludeColumns = null)
        {
            var o = options ?? new MLGuidanceOptions();
            o.Validate();
            excludeColumns ??= new HashSet<string>();

            int n = table.Count;
            // A ticker with a bit over a year of daily bars is common and shouldn't hard-fail just because
            // the requested warm-up (250 by default) leaves no room for a walk-forward checkpoint; shrink it
            // to whatever the history actually supports instead, same as Backtester does for its own warmup.
            int warmup = Math.Min(o.Warmup, n - o.Horizon - MinTrainingRows);
            if (warmup < 30)
                throw new InvalidOperationException(
                    $"not enough history: need at least {o.Horizon + MinTrainingRows + 30} bars (horizon + minimum training rows), have {n}");

            var (names, features, excluded) = BuildFeatures(table, excludeColumns);
            if (features.Length == 0)
                throw new InvalidOperationException("no usable indicator columns to train on");

            var closes = table.Closes;
            var forward = new double?[n];
            var label = new double?[n];
            for (int i = 0; i + o.Horizon < n; i++)
                if (closes[i] > 0)
                {
                    double r = closes[i + o.Horizon] / closes[i] - 1;
                    forward[i] = r;
                    label[i] = r > 0 ? 1.0 : 0.0;
                }

            var boostOptions = new BoostOptions(o.NumTrees, o.MaxDepth, MinSamplesLeaf: 15, o.LearningRate, FeatureFraction: 0.4, Seed);

            int checkpoints = 0, directional = 0, aligned = 0;
            double alignedReturnSum = 0, actualUpCount = 0, resolvedCount = 0;
            var calibration = new List<(double Predicted, double Actual)>();

            for (int t = warmup; t + o.Horizon < n; t += o.Step)
            {
                var trainRows = RowsWithKnownLabel(label, t, o.Horizon);
                if (trainRows.Length < MinTrainingRows) continue;

                var model = GradientBoostedTrees.Fit(
                    Select(features, trainRows), Select(label, trainRows), boostOptions);
                double p = model.PredictProbability(features[t]);
                checkpoints++;
                calibration.Add((p, label[t]!.Value));
                resolvedCount++;
                actualUpCount += label[t]!.Value;

                if (Math.Abs(p - 0.5) < o.NeutralBand) continue;
                directional++;
                bool bullish = p >= 0.5;
                double alignedReturn = bullish ? forward[t]!.Value : -forward[t]!.Value;
                if (alignedReturn > 0) aligned++;
                alignedReturnSum += alignedReturn;
            }

            // Final model: every bar whose outcome has fully resolved, used to read today's signal.
            var allResolved = RowsWithKnownLabel(label, n, o.Horizon);
            var finalModel = GradientBoostedTrees.Fit(Select(features, allResolved), Select(label, allResolved), boostOptions);
            double probability = finalModel.PredictProbability(features[n - 1]);
            var direction = Math.Abs(probability - 0.5) < o.NeutralBand ? SignalDirection.Neutral
                : probability >= 0.5 ? SignalDirection.Bullish : SignalDirection.Bearish;

            var importance = finalModel.FeatureImportance();
            var topFeatures = names
                .Select((name, i) => new FeatureScore(name, importance[i]))
                .Where(f => f.Importance > 0)
                .OrderByDescending(f => f.Importance)
                .Take(10)
                .ToList();

            double baseline = resolvedCount > 0 ? Math.Max(actualUpCount, resolvedCount - actualUpCount) / resolvedCount : 0;

            return new MLGuidanceReport(
                table.Ticker, o, warmup, excluded, features.Length > 0 ? features[0].Length : 0,
                table.Timestamps[^1],
                direction, Math.Abs(probability - 0.5) * 200, probability,
                topFeatures,
                checkpoints, directional, aligned,
                directional == 0 ? 0 : (double)aligned / directional,
                directional == 0 ? 0 : alignedReturnSum / directional,
                alignedReturnSum,
                baseline,
                BuildCalibration(calibration));
        }

        /// <summary>Rows whose Horizon-bar-ahead outcome is fully known by bar <paramref name="asOf"/>.</summary>
        private static int[] RowsWithKnownLabel(double?[] label, int asOf, int horizon)
        {
            var rows = new List<int>();
            for (int j = 0; j + horizon <= asOf; j++)
                if (label[j] is not null) rows.Add(j);
            return rows.ToArray();
        }

        private static double[][] Select(double[][] features, int[] rows) => rows.Select(i => features[i]).ToArray();
        private static double[] Select(double?[] label, int[] rows) => rows.Select(i => label[i]!.Value).ToArray();

        /// <summary>
        /// One row per indicator column/stance, forward-filled through
        /// warm-up gaps and — for price-level columns — expressed as distance
        /// from the close so the model generalizes across price scales.
        /// </summary>
        private static (List<string> Names, double[][] Features, List<string> Excluded) BuildFeatures(
            SnapshotTable table, IReadOnlySet<string> excludeColumns)
        {
            int n = table.Count;
            var names = new List<string>();
            var columns = new List<double[]>();
            var excluded = new List<string>();

            foreach (var (columnName, raw) in table.Columns)
            {
                if (excludeColumns.Contains(columnName)) { excluded.Add(columnName); continue; }
                bool relative = IsPriceLevel(raw, table.Closes);
                var values = new double[n];
                double? last = null;
                for (int i = 0; i < n; i++)
                {
                    double? v = raw[i] is { } x && table.Closes[i] > 0
                        ? (relative ? x / table.Closes[i] - 1 : x)
                        : null;
                    if (v is not null) last = v;
                    values[i] = last ?? 0;
                }
                if (values.Distinct().Count() < 2) continue; // constant column, no signal
                names.Add(relative ? $"{columnName} vs close" : columnName);
                columns.Add(values);
            }

            foreach (var (stanceName, stance) in table.Stances)
            {
                if (excludeColumns.Contains(stanceName)) { excluded.Add(stanceName); continue; }
                // One signed column (Bullish=1, Neutral=0, Bearish=-1) rather than two flags:
                // a tree splits on it exactly as well, at half the feature count and cost.
                names.Add(stanceName);
                columns.Add([.. Enumerable.Range(0, n).Select(i => (double)(int)ToSign(stance[i]))]);
            }

            var features = new double[n][];
            for (int i = 0; i < n; i++)
                features[i] = columns.Select(c => c[i]).ToArray();

            return (names, features, excluded);
        }

        private static int ToSign(SignalDirection d) => d switch
        {
            SignalDirection.Bullish => 1,
            SignalDirection.Bearish => -1,
            _ => 0,
        };

        /// <summary>Mirrors PatternAnalyzer's rule: a column is a price level when ~all its values sit within 0.5x-2x the close.</summary>
        private static bool IsPriceLevel(double?[] values, IReadOnlyList<double> closes)
        {
            int total = 0, near = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] is not { } v || closes[i] <= 0) continue;
                total++;
                double ratio = v / closes[i];
                if (ratio is >= 0.5 and <= 2) near++;
            }
            return total > 0 && near >= 0.9 * total;
        }

        private static List<CalibrationBucket> BuildCalibration(List<(double Predicted, double Actual)> points)
        {
            (double Lo, double Hi, string Label)[] bins =
            [
                (0.0, 0.2, "0-20%"), (0.2, 0.4, "20-40%"), (0.4, 0.6, "40-60%"),
                (0.6, 0.8, "60-80%"), (0.8, 1.01, "80-100%"),
            ];
            var buckets = new List<CalibrationBucket>();
            foreach (var (lo, hi, label) in bins)
            {
                var inBin = points.Where(p => p.Predicted >= lo && p.Predicted < hi).ToList();
                if (inBin.Count == 0) continue;
                buckets.Add(new CalibrationBucket(label, inBin.Count, inBin.Average(p => p.Predicted), inBin.Average(p => p.Actual)));
            }
            return buckets;
        }
    }
}
