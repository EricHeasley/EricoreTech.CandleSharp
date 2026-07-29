using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Standard
{
    // The standard indicator pack. Each one is a thin adapter: math from Core's
    // Indicators class, stance from a Stances helper. Built as a plugin — the
    // DLL is copied to the repo-root plugins directory and loaded at startup.

    /// <summary>Bullish while the fast SMA is above the slow SMA (golden/death cross).</summary>
    public sealed class SmaCrossoverIndicator(int fast = 20, int slow = 50) : ITechnicalIndicator
    {
        public string Name => $"SMA_cross_{fast}_{slow}";

        public IndicatorResult Compute(IReadOnlyList<Candle> candles)
        {
            var fastSma = Indicators.Sma(candles, fast);
            var slowSma = Indicators.Sma(candles, slow);
            return new IndicatorResult(
                [($"SMA_{fast}", fastSma), ($"SMA_{slow}", slowSma)],
                Stances.FromComparison(fastSma, slowSma));
        }
    }
}
