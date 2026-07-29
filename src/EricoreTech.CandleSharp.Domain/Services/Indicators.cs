namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// Technical indicators over a candle series. Every method returns an array
    /// aligned to the input (result[i] belongs to candles[i]); entries are null
    /// during the indicator's warm-up window. Smoothing matches the common
    /// pandas/TA conventions: EMA seeded on the first value, Wilder smoothing
    /// for RSI and ATR, sample standard deviation for Bollinger Bands.
    /// </summary>
    public static partial class Indicators
    {
        public static double?[] Sma(IReadOnlyList<Candle> candles, int window)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(window, 1);
            var result = new double?[candles.Count];
            double sum = 0;
            for (int i = 0; i < candles.Count; i++)
            {
                sum += candles[i].Close;
                if (i >= window) sum -= candles[i - window].Close;
                if (i >= window - 1) result[i] = sum / window;
            }
            return result;
        }

        public static double?[] Ema(IReadOnlyList<Candle> candles, int span)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(span, 1);
            return EmaOf(candles.Select(c => c.Close).ToArray(), 2.0 / (span + 1));
        }

        public static double?[] Rsi(IReadOnlyList<Candle> candles, int window = 14)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(window, 1);
            var result = new double?[candles.Count];
            if (candles.Count < 2) return result;

            double alpha = 1.0 / window;
            double avgGain = 0, avgLoss = 0;
            for (int i = 1; i < candles.Count; i++)
            {
                double delta = candles[i].Close - candles[i - 1].Close;
                double gain = Math.Max(delta, 0);
                double loss = Math.Max(-delta, 0);
                if (i == 1)
                {
                    avgGain = gain;
                    avgLoss = loss;
                }
                else
                {
                    avgGain = alpha * gain + (1 - alpha) * avgGain;
                    avgLoss = alpha * loss + (1 - alpha) * avgLoss;
                }
                result[i] = avgLoss == 0 ? 100 : 100 - 100 / (1 + avgGain / avgLoss);
            }
            return result;
        }

        public static (double?[] Macd, double?[] Signal, double?[] Histogram) Macd(
            IReadOnlyList<Candle> candles, int fast = 12, int slow = 26, int signal = 9)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(fast, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(signal, 1);
            if (fast >= slow)
                throw new ArgumentOutOfRangeException(nameof(fast), "MACD fast period must be smaller than the slow period");
            var fastEma = Ema(candles, fast);
            var slowEma = Ema(candles, slow);
            var macd = new double[candles.Count];
            for (int i = 0; i < candles.Count; i++)
                macd[i] = fastEma[i]!.Value - slowEma[i]!.Value;

            var signalLine = EmaOf(macd, 2.0 / (signal + 1));
            var macdOut = new double?[candles.Count];
            var hist = new double?[candles.Count];
            for (int i = 0; i < candles.Count; i++)
            {
                macdOut[i] = macd[i];
                hist[i] = macd[i] - signalLine[i]!.Value;
            }
            return (macdOut, signalLine, hist);
        }

        public static (double?[] Mid, double?[] Upper, double?[] Lower) BollingerBands(
            IReadOnlyList<Candle> candles, int window = 20, double numStd = 2.0)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(window, 2);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numStd);
            var mid = Sma(candles, window);
            var upper = new double?[candles.Count];
            var lower = new double?[candles.Count];
            for (int i = window - 1; i < candles.Count; i++)
            {
                double mean = mid[i]!.Value;
                double sumSq = 0;
                for (int j = i - window + 1; j <= i; j++)
                    sumSq += (candles[j].Close - mean) * (candles[j].Close - mean);
                double std = Math.Sqrt(sumSq / (window - 1));
                upper[i] = mean + numStd * std;
                lower[i] = mean - numStd * std;
            }
            return (mid, upper, lower);
        }

        public static double?[] Atr(IReadOnlyList<Candle> candles, int window = 14)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(window, 1);
            var result = new double?[candles.Count];
            if (candles.Count == 0) return result;

            double alpha = 1.0 / window;
            double atr = candles[0].High - candles[0].Low;
            result[0] = atr;
            for (int i = 1; i < candles.Count; i++)
            {
                double prevClose = candles[i - 1].Close;
                double tr = Math.Max(candles[i].High - candles[i].Low,
                    Math.Max(Math.Abs(candles[i].High - prevClose),
                             Math.Abs(candles[i].Low - prevClose)));
                atr = alpha * tr + (1 - alpha) * atr;
                result[i] = atr;
            }
            return result;
        }

        private static double?[] EmaOf(IReadOnlyList<double> values, double alpha)
        {
            var result = new double?[values.Count];
            if (values.Count == 0) return result;
            double ema = values[0];
            result[0] = ema;
            for (int i = 1; i < values.Count; i++)
            {
                ema = alpha * values[i] + (1 - alpha) * ema;
                result[i] = ema;
            }
            return result;
        }
    }
}
