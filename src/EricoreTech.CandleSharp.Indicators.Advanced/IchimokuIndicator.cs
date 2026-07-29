using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Advanced
{
    /// <summary>Ichimoku: Bullish above the cloud, Bearish below it, Neutral inside.</summary>
    public sealed class IchimokuIndicator(int tenkanW = 9, int kijunW = 26, int senkouW = 52)
        : ITechnicalIndicator
    {
        public string Name => "ICHIMOKU";

        public IndicatorResult Compute(IReadOnlyList<Candle> candles)
        {
            var (tenkan, kijun, senkouA, senkouB, chikou) =
                Indicators.Ichimoku(candles, tenkanW, kijunW, senkouW);
            var stance = new SignalDirection[candles.Count];
            for (int i = 0; i < candles.Count; i++)
            {
                if (senkouA[i] is not { } a || senkouB[i] is not { } b) continue;
                double close = candles[i].Close;
                if (close > Math.Max(a, b)) stance[i] = SignalDirection.Bullish;
                else if (close < Math.Min(a, b)) stance[i] = SignalDirection.Bearish;
            }
            return new IndicatorResult(
                [($"TENKAN_{tenkanW}", tenkan), ($"KIJUN_{kijunW}", kijun),
                 ("SENKOU_A", senkouA), ("SENKOU_B", senkouB), ("CHIKOU", chikou)],
                stance);
        }
    }
}
