using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Standard
{
    /// <summary>Bullish while the fast EMA is above the slow EMA.</summary>
    public sealed class EmaCrossoverIndicator(int fast = 12, int slow = 26) : ITechnicalIndicator
    {
        public string Name => $"EMA_cross_{fast}_{slow}";

        public IndicatorResult Compute(IReadOnlyList<Candle> candles)
        {
            var fastEma = Indicators.Ema(candles, fast);
            var slowEma = Indicators.Ema(candles, slow);
            return new IndicatorResult(
                [($"EMA_{fast}", fastEma), ($"EMA_{slow}", slowEma)],
                Stances.FromComparison(fastEma, slowEma));
        }
    }
}
