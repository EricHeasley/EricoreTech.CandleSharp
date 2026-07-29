namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// Aggregates candles into a coarser timeframe (idea borrowed from Stock
    /// Indicators for .NET's quote aggregation): fetch daily data once, derive
    /// weekly/monthly series locally instead of re-downloading. Buckets are
    /// stamped with their period start (ISO Monday for weeks, the 1st for months).
    /// </summary>
    public static class Resampler
    {
        public static List<Candle> Aggregate(IReadOnlyList<Candle> candles, AggregatePeriod period)
        {
            var result = new List<Candle>();
            Candle? bucket = null;
            DateTime bucketKey = default;

            foreach (var candle in candles)
            {
                var key = period == AggregatePeriod.Weekly
                    ? candle.Timestamp.Date.AddDays(-(((int)candle.Timestamp.DayOfWeek + 6) % 7))
                    : new DateTime(candle.Timestamp.Year, candle.Timestamp.Month, 1);

                if (bucket is null || key != bucketKey)
                {
                    if (bucket is not null) result.Add(bucket);
                    bucketKey = key;
                    bucket = candle with { Timestamp = key };
                }
                else
                {
                    bucket = bucket with
                    {
                        High = Math.Max(bucket.High, candle.High),
                        Low = Math.Min(bucket.Low, candle.Low),
                        Close = candle.Close,
                        Volume = bucket.Volume + candle.Volume,
                    };
                }
            }
            if (bucket is not null) result.Add(bucket);
            return result;
        }

        /// <summary>Maps interval names ("1wk", "1mo") to a period; null if not an aggregate interval.</summary>
        public static AggregatePeriod? FromInterval(string interval) => interval switch
        {
            "1wk" => AggregatePeriod.Weekly,
            "1mo" => AggregatePeriod.Monthly,
            _ => null,
        };
    }
}
