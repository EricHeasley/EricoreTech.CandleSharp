namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// Pure dividend math. Growth rates use complete calendar years only (the
    /// current partial year would understate growth), and multi-year rates are
    /// compound annual growth rates.
    /// </summary>
    public static class DividendAnalytics
    {
        public static DividendSummary Summarize(
            string ticker, IReadOnlyList<Dividend> dividends, IReadOnlyList<Candle> candles)
        {
            var sorted = dividends.OrderBy(d => d.Timestamp).ToList();
            if (sorted.Count == 0)
                return new DividendSummary(ticker, 0, null, null, 0, null, null, null, null, 0, [], []);

            var asOf = candles.Count > 0 ? candles[^1].Timestamp : sorted[^1].Timestamp;
            double trailingYear = sorted
                .Where(d => d.Timestamp > asOf.AddDays(-365) && d.Timestamp <= asOf)
                .Sum(d => d.Amount);
            double? yieldPercent = candles.Count > 0 && candles[^1].Close > 0
                ? trailingYear / candles[^1].Close * 100
                : null;

            var annual = sorted
                .GroupBy(d => d.Timestamp.Year)
                .Select(g => new AnnualDividend(g.Key, g.Sum(d => d.Amount)))
                .OrderBy(a => a.Year)
                .ToList();
            var fullYears = annual.Where(a => a.Year < asOf.Year).ToList();

            double? growthYoY = null;
            if (fullYears.Count >= 2 && fullYears[^1].Year - fullYears[^2].Year == 1 && fullYears[^2].Total > 0)
                growthYoY = (fullYears[^1].Total / fullYears[^2].Total - 1) * 100;

            int streak = 0;
            for (int i = fullYears.Count - 1; i > 0; i--)
            {
                if (fullYears[i].Year - fullYears[i - 1].Year != 1
                    || fullYears[i].Total <= fullYears[i - 1].Total) break;
                streak++;
            }

            return new DividendSummary(
                ticker, sorted.Count, sorted[^1].Timestamp, sorted[^1].Amount,
                trailingYear, yieldPercent,
                growthYoY, Cagr(fullYears, 3), Cagr(fullYears, 5),
                streak, annual, sorted.TakeLast(8).ToList());
        }

        private static double? Cagr(List<AnnualDividend> fullYears, int span)
        {
            if (fullYears.Count == 0) return null;
            var last = fullYears[^1];
            var baseline = fullYears.FirstOrDefault(a => a.Year == last.Year - span);
            if (baseline is null || baseline.Total <= 0) return null;
            return (Math.Pow(last.Total / baseline.Total, 1.0 / span) - 1) * 100;
        }
    }
}
