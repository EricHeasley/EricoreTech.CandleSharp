using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Advanced
{
    // The advanced indicator pack. Same pattern as the standard pack: math from
    // Core's Indicators class, stance mapped through the uniform contract, built
    // as a plugin DLL.
    // These show the range the stance model covers — trend-strength filters
    // (ADX), multi-line systems (Ichimoku), stateful trailing stops (PSAR,
    // SuperTrend), volume-weighted oscillators (MFI), and breakouts (Donchian).

    /// <summary>Slow stochastic; mean-reversion stance at the 20/80 thresholds on %K.</summary>
    public sealed class StochasticIndicator(int window = 14, int smooth = 3, int dPeriod = 3,
        double oversold = 20, double overbought = 80) : ITechnicalIndicator
    {
        public string Name => $"STOCH_{window}_{smooth}_{dPeriod}";

        public IndicatorResult Compute(IReadOnlyList<Candle> candles)
        {
            var (k, d) = Indicators.Stochastic(candles, window, smooth, dPeriod);
            return new IndicatorResult(
                [("STOCH_K", k), ("STOCH_D", d)],
                Stances.FromThresholds(k, bullishBelow: oversold, bearishAbove: overbought));
        }
    }
}
