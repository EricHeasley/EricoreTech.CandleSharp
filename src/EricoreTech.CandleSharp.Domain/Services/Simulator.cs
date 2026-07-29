namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// Buy-and-hold simulation. Stored candle prices are dividend-adjusted, so
    /// simulating cash dividends on them would double count; the simulator
    /// first reconstructs the raw (unadjusted) close series from the adjusted
    /// prices and the dividend history using the standard adjustment factor
    /// f = rawPrevClose is adjusted by (rawPrev - D) / rawPrev per ex-date,
    /// inverted here as f = adjPrev / (adjPrev + D * F). The adjusted series
    /// itself gives the dividends-reinvested value exactly.
    /// (Caveat: dividend amounts are as-paid; a stock split inside the window
    /// would skew the cash-dividend leg — rare within a one-year window.)
    /// </summary>
    public static class Simulator
    {
        public static SimulationResult BuyAndHold(
            string ticker,
            IReadOnlyList<Candle> candles,
            IReadOnlyList<Dividend> dividends,
            double investment,
            DateTime start,
            DateTime end)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(investment);
            if (end <= start)
                throw new ArgumentException("End date must be after the start date.");

            int startIndex = -1, endIndex = -1;
            for (int i = 0; i < candles.Count; i++)
            {
                if (startIndex < 0 && candles[i].Timestamp >= start) startIndex = i;
                if (candles[i].Timestamp <= end) endIndex = i;
            }
            if (startIndex < 0 || endIndex <= startIndex)
                throw new ArgumentException(
                    $"Not enough data between {start:yyyy-MM-dd} and {end:yyyy-MM-dd} to simulate.");

            var raw = ReconstructRawCloses(candles, dividends);
            double buyPrice = raw[startIndex];
            double sellPrice = raw[endIndex];
            double shares = investment / buyPrice;

            double dividendCash = 0;
            int payments = 0;
            var buyDate = candles[startIndex].Timestamp;
            var sellDate = candles[endIndex].Timestamp;
            foreach (var dividend in dividends)
            {
                if (dividend.Timestamp <= buyDate || dividend.Timestamp > sellDate) continue;
                dividendCash += shares * dividend.Amount;
                payments++;
            }

            double endStockValue = shares * sellPrice;
            double endTotalValue = endStockValue + dividendCash;
            double reinvestedValue = investment * (candles[endIndex].Close / candles[startIndex].Close);

            return new SimulationResult(
                ticker, buyDate, sellDate, investment, shares, buyPrice, sellPrice,
                endStockValue, dividendCash, payments, endTotalValue,
                (endTotalValue - investment) / investment * 100,
                (endStockValue - investment) / investment * 100,
                dividendCash / investment * 100,
                reinvestedValue,
                (reinvestedValue - investment) / investment * 100);
        }

        /// <summary>Undo the dividend adjustment: raw[i] = adjusted[i] / F(i), F built back-to-front.</summary>
        public static double[] ReconstructRawCloses(
            IReadOnlyList<Candle> candles, IReadOnlyList<Dividend> dividends)
        {
            int n = candles.Count;
            var raw = new double[n];
            if (n == 0) return raw;

            // Map each dividend to its ex-dividend bar (first bar at or after the ex-date).
            var byExBar = new Dictionary<int, List<double>>();
            foreach (var dividend in dividends)
            {
                int exBar = -1;
                for (int i = 0; i < n; i++)
                    if (candles[i].Timestamp >= dividend.Timestamp) { exBar = i; break; }
                if (exBar <= 0) continue;   // outside range, or nothing precedes it
                if (!byExBar.TryGetValue(exBar, out var list)) byExBar[exBar] = list = [];
                list.Add(dividend.Amount);
            }

            double factor = 1;
            for (int i = n - 1; i >= 0; i--)
            {
                raw[i] = candles[i].Close / factor;
                if (i > 0 && byExBar.TryGetValue(i, out var amounts))
                    foreach (var amount in amounts)
                    {
                        double adjPrev = candles[i - 1].Close;
                        factor *= adjPrev / (adjPrev + amount * factor);
                    }
            }
            return raw;
        }
    }
}
