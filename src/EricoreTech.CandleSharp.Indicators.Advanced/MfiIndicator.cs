using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Advanced
{
    /// <summary>Money Flow Index: volume-weighted mean reversion at 20/80.</summary>
    public sealed class MfiIndicator(int window = 14, double oversold = 20, double overbought = 80)
        : ITechnicalIndicator
    {
        public string Name => $"MFI_{window}";

        public IndicatorResult Compute(IReadOnlyList<Candle> candles)
        {
            var mfi = Indicators.Mfi(candles, window);
            return new IndicatorResult(
                [($"MFI_{window}", mfi)],
                Stances.FromThresholds(mfi, bullishBelow: oversold, bearishAbove: overbought));
        }
    }
}
