using System.Globalization;
using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Agents
{
    // Machine-learning agents, dependency-free by design. Both learn only from
    // the past relative to the bar they predict: the logistic model's training
    // labels all lie in loaded history, and the Hedge meta-agent replays earlier
    // checkpoints using truncated inputs. For fully honest evaluation, run them
    // through the Backtester, which re-truncates everything walk-forward.

    /// <summary>
    /// Logistic regression over indicator-derived features, trained on this
    /// ticker's own history with plain gradient descent. The predicted
    /// probability of an up-move over the horizon IS the confidence, and the
    /// learned weights double as the scorecard.
    /// </summary>
    public sealed class LogisticRegressionAgent(
        int horizon = 10, double neutralBand = 0.05, int minSamples = 50) : ITradingAgent
    {
        private const int FeatureWarmup = 30;

        private static readonly string[] FeatureNames =
            ["RSI", "Stoch %K", "ADX", "MACD hist %", "BB position", "ROC 10", "ATR ratio"];

        public string Key => "ml_logistic";
        public string DisplayName => "ML Logistic";

        public AgentReport Analyze(AgentInput input)
        {
            var candles = input.Candles;
            int n = candles.Count;
            var rsi = SeriesLookup.Column(input, "RSI_");
            var stochK = SeriesLookup.Column(input, "STOCH_K");
            var adx = SeriesLookup.Column(input, "ADX_");
            var macdHist = SeriesLookup.Column(input, "MACD_hist");
            var bbUpper = SeriesLookup.Column(input, "BB_upper");
            var bbLower = SeriesLookup.Column(input, "BB_lower");
            var atr = SeriesLookup.Column(input, "ATR_");

            double[] Features(int i)
            {
                double close = Math.Max(candles[i].Close, 1e-9);
                double bbPosition = 0;
                if (bbUpper?[i] is { } u && bbLower?[i] is { } l && u > l)
                    bbPosition = Math.Clamp((candles[i].Close - l) / (u - l), 0, 1) - 0.5;
                return
                [
                    (rsi?[i] ?? 50) / 100 - 0.5,
                    (stochK?[i] ?? 50) / 100 - 0.5,
                    (adx?[i] ?? 0) / 100,
                    (macdHist?[i] ?? 0) / close * 100,
                    bbPosition,
                    i >= 10 ? candles[i].Close / Math.Max(candles[i - 10].Close, 1e-9) - 1 : 0,
                    (atr?[i] ?? 0) / close,
                ];
            }

            var xs = new List<double[]>();
            var ys = new List<double>();
            for (int i = FeatureWarmup; i + horizon < n; i++)
            {
                xs.Add(Features(i));
                ys.Add(candles[i + horizon].Close > candles[i].Close ? 1 : 0);
            }
            if (xs.Count < minSamples)
                return new AgentReport(Key, DisplayName, input.Ticker,
                    new TradeSignal(SignalDirection.Neutral, 0,
                        $"Only {xs.Count} training samples; need {minSamples}. Load more history."),
                    []);

            int f = FeatureNames.Length;
            var mean = new double[f];
            var std = new double[f];
            foreach (var x in xs)
                for (int j = 0; j < f; j++) mean[j] += x[j];
            for (int j = 0; j < f; j++) mean[j] /= xs.Count;
            foreach (var x in xs)
                for (int j = 0; j < f; j++) std[j] += (x[j] - mean[j]) * (x[j] - mean[j]);
            for (int j = 0; j < f; j++) std[j] = Math.Max(Math.Sqrt(std[j] / xs.Count), 1e-9);

            double[] Standardize(double[] x)
            {
                var z = new double[f];
                for (int j = 0; j < f; j++) z[j] = (x[j] - mean[j]) / std[j];
                return z;
            }
            var trainX = xs.Select(Standardize).ToList();

            // Full-batch gradient descent with light L2; deterministic zero init.
            var weights = new double[f + 1];
            const double learningRate = 0.1, l2 = 0.001;
            const int epochs = 500;
            for (int epoch = 0; epoch < epochs; epoch++)
            {
                var gradient = new double[f + 1];
                for (int s = 0; s < trainX.Count; s++)
                {
                    double error = Predict(trainX[s]) - ys[s];
                    for (int j = 0; j < f; j++) gradient[j] += error * trainX[s][j];
                    gradient[f] += error;
                }
                for (int j = 0; j <= f; j++)
                {
                    double reg = j < f ? l2 * weights[j] : 0;
                    weights[j] -= learningRate * (gradient[j] / trainX.Count + reg);
                }
            }

            int correct = 0;
            for (int s = 0; s < trainX.Count; s++)
                if ((Predict(trainX[s]) >= 0.5 ? 1 : 0) == (int)ys[s]) correct++;
            double accuracy = (double)correct / trainX.Count;

            double p = Predict(Standardize(Features(n - 1)));
            var direction = p > 0.5 + neutralBand ? SignalDirection.Bullish
                : p < 0.5 - neutralBand ? SignalDirection.Bearish
                : SignalDirection.Neutral;
            double confidence = Math.Min(100, Math.Round(Math.Abs(p - 0.5) * 200));

            var details = new List<string>
            {
                string.Create(CultureInfo.InvariantCulture,
                    $"Trained on {trainX.Count} samples ({horizon}-bar horizon), in-sample accuracy {accuracy:P0}"),
                string.Create(CultureInfo.InvariantCulture, $"P(up over next {horizon} bars) = {p:P0}"),
            };
            details.AddRange(Enumerable.Range(0, f)
                .OrderByDescending(j => Math.Abs(weights[j]))
                .Take(4)
                .Select(j => string.Create(CultureInfo.InvariantCulture,
                    $"Weight {FeatureNames[j]}: {weights[j]:+0.00;-0.00}")));

            return new AgentReport(Key, DisplayName, input.Ticker,
                new TradeSignal(direction, confidence, string.Create(CultureInfo.InvariantCulture,
                    $"Model puts P(up over {horizon} bars) at {p:P0} (in-sample accuracy {accuracy:P0} on {trainX.Count} samples).")),
                [new ScoreCard("Model", Math.Round(p * 100), 100, details)]);

            double Predict(double[] z)
            {
                double sum = weights[f];
                for (int j = 0; j < f; j++) sum += weights[j] * z[j];
                return 1.0 / (1.0 + Math.Exp(-Math.Clamp(sum, -30, 30)));
            }
        }
    }
}
