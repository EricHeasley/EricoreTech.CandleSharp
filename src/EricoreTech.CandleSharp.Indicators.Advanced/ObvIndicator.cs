using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Advanced
{
    /// <summary>
    /// On-Balance Volume: cumulative volume signed by the close-to-close
    /// direction; stance is Bullish while OBV is above its own moving average.
    /// </summary>
    public sealed class ObvIndicator(int smaWindow = 20) : ITechnicalIndicator
    {
        public string Name => $"OBV_{smaWindow}";

        public IndicatorResult Compute(IReadOnlyList<Candle> candles)
        {
            var obv = new double?[candles.Count];
            double running = 0;
            for (int i = 0; i < candles.Count; i++)
            {
                if (i > 0)
                {
                    double delta = candles[i].Close - candles[i - 1].Close;
                    running += delta > 0 ? candles[i].Volume : delta < 0 ? -candles[i].Volume : 0;
                }
                obv[i] = running;
            }

            var obvSma = new double?[candles.Count];
            double sum = 0;
            for (int i = 0; i < candles.Count; i++)
            {
                sum += obv[i]!.Value;
                if (i >= smaWindow) sum -= obv[i - smaWindow]!.Value;
                if (i >= smaWindow - 1) obvSma[i] = sum / smaWindow;
            }

            return new IndicatorResult(
                [("OBV", obv), ($"OBV_SMA_{smaWindow}", obvSma)],
                Stances.FromComparison(obv, obvSma));
        }
    }
}
