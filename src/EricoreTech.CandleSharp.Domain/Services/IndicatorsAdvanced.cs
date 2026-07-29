namespace EricoreTech.CandleSharp.Domain
{
    // Advanced indicator math: multi-line, stateful, and volume-weighted studies.
    // Same conventions as Indicators.cs — candle-aligned arrays, null during
    // warm-up, Wilder smoothing seeded on the first value where applicable.
    public static partial class Indicators
    {
        /// <summary>Slow Stochastic Oscillator: %K (smoothed) and %D (SMA of %K), both 0..100.</summary>
        public static (double?[] K, double?[] D) Stochastic(
            IReadOnlyList<Candle> candles, int window = 14, int smooth = 3, int dPeriod = 3)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(window, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(smooth, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(dPeriod, 1);
            var fastK = new double?[candles.Count];
            for (int i = window - 1; i < candles.Count; i++)
            {
                var (hh, ll) = HighLow(candles, i - window + 1, i);
                fastK[i] = hh == ll ? 50 : (candles[i].Close - ll) / (hh - ll) * 100;
            }
            var k = SmaOf(fastK, smooth);
            var d = SmaOf(k, dPeriod);
            return (k, d);
        }

        /// <summary>ADX with +DI/-DI (Wilder). ADX measures trend strength, the DIs its direction.</summary>
        public static (double?[] Adx, double?[] PlusDi, double?[] MinusDi) Adx(
            IReadOnlyList<Candle> candles, int window = 14)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(window, 1);
            int n = candles.Count;
            var adx = new double?[n];
            var plusDi = new double?[n];
            var minusDi = new double?[n];
            if (n < 2) return (adx, plusDi, minusDi);

            double alpha = 1.0 / window;
            double sTr = 0, sPlus = 0, sMinus = 0, adxVal = 0;
            bool adxSeeded = false;

            for (int i = 1; i < n; i++)
            {
                double upMove = candles[i].High - candles[i - 1].High;
                double downMove = candles[i - 1].Low - candles[i].Low;
                double plusDm = upMove > downMove && upMove > 0 ? upMove : 0;
                double minusDm = downMove > upMove && downMove > 0 ? downMove : 0;
                double prevClose = candles[i - 1].Close;
                double tr = Math.Max(candles[i].High - candles[i].Low,
                    Math.Max(Math.Abs(candles[i].High - prevClose), Math.Abs(candles[i].Low - prevClose)));

                if (i == 1) { sTr = tr; sPlus = plusDm; sMinus = minusDm; }
                else
                {
                    sTr = alpha * tr + (1 - alpha) * sTr;
                    sPlus = alpha * plusDm + (1 - alpha) * sPlus;
                    sMinus = alpha * minusDm + (1 - alpha) * sMinus;
                }

                double pdi = sTr == 0 ? 0 : 100 * sPlus / sTr;
                double mdi = sTr == 0 ? 0 : 100 * sMinus / sTr;
                plusDi[i] = pdi;
                minusDi[i] = mdi;

                double dx = pdi + mdi == 0 ? 0 : 100 * Math.Abs(pdi - mdi) / (pdi + mdi);
                if (!adxSeeded) { adxVal = dx; adxSeeded = true; }
                else adxVal = alpha * dx + (1 - alpha) * adxVal;
                adx[i] = adxVal;
            }
            return (adx, plusDi, minusDi);
        }

        /// <summary>
        /// Ichimoku Cloud. Senkou spans are aligned to the bar they APPLY to (already
        /// shifted forward), so cloud position can be read off the same index.
        /// Chikou is today's close shifted back; its last `shift` entries are null.
        /// </summary>
        public static (double?[] Tenkan, double?[] Kijun, double?[] SenkouA, double?[] SenkouB, double?[] Chikou)
            Ichimoku(IReadOnlyList<Candle> candles, int tenkanW = 9, int kijunW = 26, int senkouW = 52, int shift = 26)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(tenkanW, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(kijunW, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(senkouW, 1);
            ArgumentOutOfRangeException.ThrowIfNegative(shift);
            int n = candles.Count;
            var tenkan = Midline(candles, tenkanW);
            var kijun = Midline(candles, kijunW);
            var senkouBRaw = Midline(candles, senkouW);

            var senkouA = new double?[n];
            var senkouB = new double?[n];
            var chikou = new double?[n];
            for (int i = 0; i < n; i++)
            {
                if (i - shift >= 0)
                {
                    if (tenkan[i - shift] is { } t && kijun[i - shift] is { } k) senkouA[i] = (t + k) / 2;
                    senkouB[i] = senkouBRaw[i - shift];
                }
                if (i + shift < n) chikou[i] = candles[i + shift].Close;
            }
            return (tenkan, kijun, senkouA, senkouB, chikou);
        }

        /// <summary>Parabolic SAR (Wilder). Below price in uptrends, above in downtrends.</summary>
        public static double?[] ParabolicSar(
            IReadOnlyList<Candle> candles, double step = 0.02, double maxStep = 0.2)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxStep, step);
            int n = candles.Count;
            var result = new double?[n];
            if (n < 2) return result;

            bool uptrend = candles[1].Close >= candles[0].Close;
            double sar = uptrend ? candles[0].Low : candles[0].High;
            double ep = uptrend ? Math.Max(candles[0].High, candles[1].High)
                                : Math.Min(candles[0].Low, candles[1].Low);
            double af = step;

            for (int i = 1; i < n; i++)
            {
                sar += af * (ep - sar);
                // SAR may never enter the prior two bars' range.
                if (uptrend)
                    sar = Math.Min(sar, Math.Min(candles[i - 1].Low, candles[Math.Max(0, i - 2)].Low));
                else
                    sar = Math.Max(sar, Math.Max(candles[i - 1].High, candles[Math.Max(0, i - 2)].High));

                if (uptrend && candles[i].Low < sar)
                {
                    uptrend = false;
                    sar = ep;
                    ep = candles[i].Low;
                    af = step;
                }
                else if (!uptrend && candles[i].High > sar)
                {
                    uptrend = true;
                    sar = ep;
                    ep = candles[i].High;
                    af = step;
                }
                else if (uptrend && candles[i].High > ep) { ep = candles[i].High; af = Math.Min(maxStep, af + step); }
                else if (!uptrend && candles[i].Low < ep) { ep = candles[i].Low; af = Math.Min(maxStep, af + step); }

                result[i] = sar;
            }
            return result;
        }

        /// <summary>
        /// SuperTrend: ATR bands that ratchet toward price and flip on a band break.
        /// Returns the line plus direction (+1 up / -1 down) per bar.
        /// </summary>
        public static (double?[] Line, int[] Direction) SuperTrend(
            IReadOnlyList<Candle> candles, int window = 10, double multiplier = 3.0)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(window, 1);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(multiplier);
            int n = candles.Count;
            var line = new double?[n];
            var direction = new int[n];
            var atr = Atr(candles, window);
            if (n == 0) return (line, direction);

            double upper = 0, lower = 0;
            int dir = 1;
            for (int i = 0; i < n; i++)
            {
                double hl2 = (candles[i].High + candles[i].Low) / 2;
                double a = atr[i]!.Value;
                double rawUpper = hl2 + multiplier * a;
                double rawLower = hl2 - multiplier * a;

                if (i == 0) { upper = rawUpper; lower = rawLower; }
                else
                {
                    upper = rawUpper < upper || candles[i - 1].Close > upper ? rawUpper : upper;
                    lower = rawLower > lower || candles[i - 1].Close < lower ? rawLower : lower;
                    if (candles[i].Close > upper) dir = 1;
                    else if (candles[i].Close < lower) dir = -1;
                }
                direction[i] = dir;
                line[i] = dir == 1 ? lower : upper;
            }
            return (line, direction);
        }

        /// <summary>Money Flow Index: volume-weighted RSI over typical price, 0..100.</summary>
        public static double?[] Mfi(IReadOnlyList<Candle> candles, int window = 14)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(window, 1);
            int n = candles.Count;
            var result = new double?[n];
            var posFlow = new double[n];
            var negFlow = new double[n];
            for (int i = 1; i < n; i++)
            {
                double tp = TypicalPrice(candles[i]);
                double prevTp = TypicalPrice(candles[i - 1]);
                double flow = tp * candles[i].Volume;
                if (tp > prevTp) posFlow[i] = flow;
                else if (tp < prevTp) negFlow[i] = flow;
            }
            double pos = 0, neg = 0;
            for (int i = 1; i < n; i++)
            {
                pos += posFlow[i];
                neg += negFlow[i];
                if (i > window) { pos -= posFlow[i - window]; neg -= negFlow[i - window]; }
                if (i >= window)
                    result[i] = pos + neg == 0 ? 50 : neg == 0 ? 100 : 100 - 100 / (1 + pos / neg);
            }
            return result;
        }

        /// <summary>Commodity Channel Index: typical-price deviation from its mean, unbounded (±100 typical).</summary>
        public static double?[] Cci(IReadOnlyList<Candle> candles, int window = 20)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(window, 1);
            int n = candles.Count;
            var result = new double?[n];
            var tp = new double[n];
            for (int i = 0; i < n; i++) tp[i] = TypicalPrice(candles[i]);

            for (int i = window - 1; i < n; i++)
            {
                double mean = 0;
                for (int j = i - window + 1; j <= i; j++) mean += tp[j];
                mean /= window;
                double meanDev = 0;
                for (int j = i - window + 1; j <= i; j++) meanDev += Math.Abs(tp[j] - mean);
                meanDev /= window;
                result[i] = meanDev == 0 ? 0 : (tp[i] - mean) / (0.015 * meanDev);
            }
            return result;
        }

        /// <summary>Donchian Channels: highest high / lowest low of the window, plus midline.</summary>
        public static (double?[] Upper, double?[] Mid, double?[] Lower) Donchian(
            IReadOnlyList<Candle> candles, int window = 20)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(window, 1);
            int n = candles.Count;
            var upper = new double?[n];
            var mid = new double?[n];
            var lower = new double?[n];
            for (int i = window - 1; i < n; i++)
            {
                var (hh, ll) = HighLow(candles, i - window + 1, i);
                upper[i] = hh;
                lower[i] = ll;
                mid[i] = (hh + ll) / 2;
            }
            return (upper, mid, lower);
        }

        private static double TypicalPrice(Candle c) => (c.High + c.Low + c.Close) / 3;

        private static (double High, double Low) HighLow(IReadOnlyList<Candle> candles, int from, int to)
        {
            double hh = double.MinValue, ll = double.MaxValue;
            for (int j = from; j <= to; j++)
            {
                if (candles[j].High > hh) hh = candles[j].High;
                if (candles[j].Low < ll) ll = candles[j].Low;
            }
            return (hh, ll);
        }

        /// <summary>(highest high + lowest low) / 2 over a window — the Ichimoku building block.</summary>
        private static double?[] Midline(IReadOnlyList<Candle> candles, int window)
        {
            var result = new double?[candles.Count];
            for (int i = window - 1; i < candles.Count; i++)
            {
                var (hh, ll) = HighLow(candles, i - window + 1, i);
                result[i] = (hh + ll) / 2;
            }
            return result;
        }

        /// <summary>SMA over a nullable series, ignoring the warm-up prefix.</summary>
        private static double?[] SmaOf(double?[] values, int window)
        {
            var result = new double?[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] is null) continue;
                double sum = 0;
                int count = 0;
                for (int j = i; j > i - window && j >= 0 && values[j] is { } v; j--) { sum += v; count++; }
                if (count == window) result[i] = sum / window;
            }
            return result;
        }
    }
}
