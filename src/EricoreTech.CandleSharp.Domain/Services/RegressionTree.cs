namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// A single CART regression tree, fit by greedy SSE-minimizing splits.
    /// This is the one building block <see cref="GradientBoostedTrees"/> fits
    /// many of, each one on the previous ensemble's residuals — the same idea
    /// as scikit-learn's GradientBoostingClassifier or a small LightGBM,
    /// written from scratch to keep the project dependency-free.
    ///
    /// Each split searches only a random subset of features (like a random
    /// forest's max_features) rather than every feature — with ~30 correlated
    /// indicator columns, that cuts split-search cost by several times and
    /// stops any single tree from leaning on the same few features.
    /// </summary>
    public sealed class RegressionTree
    {
        private readonly Node _root;
        private readonly double[] _featureGain;

        private RegressionTree(Node root, double[] featureGain)
        {
            _root = root;
            _featureGain = featureGain;
        }

        /// <summary>Total SSE reduction this tree credits to each feature index, for importance ranking.</summary>
        public IReadOnlyList<double> FeatureGain => _featureGain;

        public double Predict(double[] x)
        {
            var node = _root;
            while (!node.IsLeaf)
                node = x[node.FeatureIndex] <= node.Threshold ? node.Left! : node.Right!;
            return node.Value;
        }

        public static RegressionTree Fit(
            double[][] x, double[] y, int[] rows, double[] sampleWeight,
            int maxDepth, int minSamplesLeaf, int featuresPerSplit, Random rng)
        {
            int featureCount = x.Length == 0 ? 0 : x[0].Length;
            var gain = new double[featureCount];
            var root = Build(x, y, rows, sampleWeight, 0, maxDepth, minSamplesLeaf, featuresPerSplit, rng, gain);
            return new RegressionTree(root, gain);
        }

        private static Node Build(
            double[][] x, double[] y, int[] rows, double[] w, int depth,
            int maxDepth, int minLeaf, int featuresPerSplit, Random rng, double[] gain)
        {
            double weightSum = 0, ySum = 0;
            foreach (var i in rows) { weightSum += w[i]; ySum += w[i] * y[i]; }
            double mean = weightSum > 0 ? ySum / weightSum : 0;
            var leaf = new Node(mean);
            if (depth >= maxDepth || rows.Length < 2 * minLeaf) return leaf;

            double parentSse = WeightedSse(rows, y, w, mean);
            if (parentSse <= 1e-12) return leaf;

            int featureCount = x[0].Length;
            var candidates = ChooseFeatures(featureCount, featuresPerSplit, rng);

            int bestFeature = -1;
            double bestThreshold = 0, bestGain = 1e-9;
            int[]? bestLeft = null, bestRight = null;

            foreach (var f in candidates)
            {
                // Array.Sort(keys, items) sorts by comparing primitives directly (no
                // per-comparison delegate call), which matters a lot here: this runs
                // once per candidate feature at every node of every tree.
                var keys = new double[rows.Length];
                var sorted = (int[])rows.Clone();
                for (int k = 0; k < rows.Length; k++) keys[k] = x[sorted[k]][f];
                Array.Sort(keys, sorted);

                double sumAll = 0, sqAll = 0, wAll = 0;
                foreach (var i in sorted) { double wi = w[i]; sumAll += wi * y[i]; sqAll += wi * y[i] * y[i]; wAll += wi; }

                double sumLeft = 0, sqLeft = 0, wLeft = 0;
                for (int k = 0; k < sorted.Length - 1; k++)
                {
                    int i = sorted[k];
                    sumLeft += w[i] * y[i]; sqLeft += w[i] * y[i] * y[i]; wLeft += w[i];
                    int nLeft = k + 1, nRight = sorted.Length - nLeft;
                    if (nLeft < minLeaf || nRight < minLeaf) continue;
                    if (x[i][f] == x[sorted[k + 1]][f]) continue; // can't split between equal values

                    double sseLeft = wLeft > 0 ? sqLeft - sumLeft * sumLeft / wLeft : 0;
                    double wRight = wAll - wLeft, sumRight = sumAll - sumLeft, sqRight = sqAll - sqLeft;
                    double sseRight = wRight > 0 ? sqRight - sumRight * sumRight / wRight : 0;
                    double g = parentSse - (sseLeft + sseRight);
                    if (g > bestGain)
                    {
                        bestGain = g;
                        bestFeature = f;
                        bestThreshold = (x[i][f] + x[sorted[k + 1]][f]) / 2;
                        bestLeft = sorted[..nLeft];
                        bestRight = sorted[nLeft..];
                    }
                }
            }

            if (bestFeature < 0) return leaf;
            gain[bestFeature] += bestGain;
            return new Node(bestFeature, bestThreshold,
                Build(x, y, bestLeft!, w, depth + 1, maxDepth, minLeaf, featuresPerSplit, rng, gain),
                Build(x, y, bestRight!, w, depth + 1, maxDepth, minLeaf, featuresPerSplit, rng, gain));
        }

        private static int[] ChooseFeatures(int featureCount, int take, Random rng)
        {
            take = Math.Clamp(take, 1, featureCount);
            if (take >= featureCount) return Enumerable.Range(0, featureCount).ToArray();
            var all = Enumerable.Range(0, featureCount).ToArray();
            for (int i = 0; i < take; i++)
            {
                int j = i + rng.Next(all.Length - i);
                (all[i], all[j]) = (all[j], all[i]);
            }
            return all[..take];
        }

        private static double WeightedSse(int[] rows, double[] y, double[] w, double mean)
        {
            double sse = 0;
            foreach (var i in rows) sse += w[i] * (y[i] - mean) * (y[i] - mean);
            return sse;
        }

        private sealed class Node
        {
            public Node(double value) => Value = value;

            public Node(int featureIndex, double threshold, Node left, Node right)
            {
                FeatureIndex = featureIndex;
                Threshold = threshold;
                Left = left;
                Right = right;
            }

            public int FeatureIndex { get; } = -1;
            public double Threshold { get; }
            public double Value { get; }
            public Node? Left { get; }
            public Node? Right { get; }
            public bool IsLeaf => Left is null;
        }
    }
}
