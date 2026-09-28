namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>Settings for one <see cref="GradientBoostedTrees"/> fit.</summary>
    public sealed record BoostOptions(
        int NumTrees = 25,
        int MaxDepth = 3,
        int MinSamplesLeaf = 15,
        double LearningRate = 0.15,
        double FeatureFraction = 0.6,
        int Seed = 1)
    {
        public void Validate()
        {
            if (NumTrees < 1) throw new ArgumentOutOfRangeException(nameof(NumTrees));
            if (MaxDepth < 1) throw new ArgumentOutOfRangeException(nameof(MaxDepth));
            if (MinSamplesLeaf < 1) throw new ArgumentOutOfRangeException(nameof(MinSamplesLeaf));
            if (LearningRate <= 0) throw new ArgumentOutOfRangeException(nameof(LearningRate));
            if (FeatureFraction is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(FeatureFraction));
        }
    }

    /// <summary>
    /// Gradient-boosted trees for binary classification (up vs. not-up), fit
    /// by the standard recipe: start from the log-odds of the base rate, then
    /// repeatedly fit a shallow <see cref="RegressionTree"/> to the current
    /// residuals (y minus the ensemble's predicted probability so far) and add
    /// it in at a small learning rate. Predicting is just summing every tree's
    /// vote through a sigmoid. Dependency-free by design, matching the rest of
    /// this project's math (Indicators.cs, the agents) — no ML.NET, no ONNX.
    /// </summary>
    public sealed class GradientBoostedTrees
    {
        private readonly double _baseScore;
        private readonly List<RegressionTree> _trees;
        private readonly double _learningRate;
        private readonly int _featureCount;

        private GradientBoostedTrees(double baseScore, List<RegressionTree> trees, double learningRate, int featureCount)
        {
            _baseScore = baseScore;
            _trees = trees;
            _learningRate = learningRate;
            _featureCount = featureCount;
        }

        public static GradientBoostedTrees Fit(double[][] x, double[] y, BoostOptions? options = null)
        {
            var o = options ?? new BoostOptions();
            o.Validate();
            if (x.Length == 0 || x.Length != y.Length)
                throw new ArgumentException("need at least one training row, with matching feature/label counts");

            int n = x.Length, featureCount = x[0].Length;
            var rng = new Random(o.Seed);
            var rows = Enumerable.Range(0, n).ToArray();

            double baseRate = Math.Clamp(y.Average(), 0.01, 0.99);
            double score = Math.Log(baseRate / (1 - baseRate)); // log-odds baseline
            var f = Enumerable.Repeat(score, n).ToArray(); // running raw score per row
            var weight = Enumerable.Repeat(1.0, n).ToArray();
            int featuresPerSplit = Math.Max(1, (int)Math.Round(featureCount * o.FeatureFraction));

            var trees = new List<RegressionTree>(o.NumTrees);
            for (int m = 0; m < o.NumTrees; m++)
            {
                var residual = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double p = Sigmoid(f[i]);
                    residual[i] = y[i] - p; // negative gradient of log-loss
                }
                var tree = RegressionTree.Fit(x, residual, rows, weight, o.MaxDepth, o.MinSamplesLeaf, featuresPerSplit, rng);
                for (int i = 0; i < n; i++) f[i] += o.LearningRate * tree.Predict(x[i]);
                trees.Add(tree);
            }

            return new GradientBoostedTrees(score, trees, o.LearningRate, featureCount);
        }

        /// <summary>Predicted probability of the positive class (label 1) for one row of features.</summary>
        public double PredictProbability(double[] x)
        {
            double f = _baseScore;
            foreach (var tree in _trees) f += _learningRate * tree.Predict(x);
            return Sigmoid(f);
        }

        /// <summary>Each feature's share of total SSE reduction across every tree, summing to 1 (0 if untouched).</summary>
        public double[] FeatureImportance()
        {
            var total = new double[_featureCount];
            foreach (var tree in _trees)
            {
                var gain = tree.FeatureGain;
                for (int i = 0; i < _featureCount; i++) total[i] += gain[i];
            }
            double sum = total.Sum();
            if (sum <= 0) return total;
            for (int i = 0; i < _featureCount; i++) total[i] /= sum;
            return total;
        }

        private static double Sigmoid(double x) => 1 / (1 + Math.Exp(-Math.Clamp(x, -30, 30)));
    }
}
