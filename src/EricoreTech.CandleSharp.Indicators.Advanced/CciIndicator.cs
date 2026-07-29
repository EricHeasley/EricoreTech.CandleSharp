using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Advanced
{
    /// <summary>CCI: mean reversion outside the ±threshold band (default ±100).</summary>
    public sealed class CciIndicator(int window = 20, double threshold = 100) : ITechnicalIndicator
    {
        public string Name => $"CCI_{window}";

        public IndicatorResult Compute(IReadOnlyList<Candle> candles)
        {
            var cci = Indicators.Cci(candles, window);
            return new IndicatorResult(
                [($"CCI_{window}", cci)],
                Stances.FromThresholds(cci, bullishBelow: -threshold, bearishAbove: threshold));
        }
    }
}
