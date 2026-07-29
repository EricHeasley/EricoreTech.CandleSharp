using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Advanced
{
    /// <summary>Parabolic SAR trailing stop: Bullish while price is above the SAR.</summary>
    public sealed class ParabolicSarIndicator(double step = 0.02, double maxStep = 0.2) : ITechnicalIndicator
    {
        public string Name => "PSAR";

        public IndicatorResult Compute(IReadOnlyList<Candle> candles)
        {
            var sar = Indicators.ParabolicSar(candles, step, maxStep);
            var close = new double?[candles.Count];
            for (int i = 0; i < candles.Count; i++) close[i] = candles[i].Close;
            return new IndicatorResult([("PSAR", sar)], Stances.FromComparison(close, sar));
        }
    }
}
