using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Standard
{
    /// <summary>Volatility only — contributes a column but never a directional stance.</summary>
    public sealed class AtrIndicator(int window = 14) : ITechnicalIndicator
    {
        public string Name => $"ATR_{window}";

        public IndicatorResult Compute(IReadOnlyList<Candle> candles) =>
            new([($"ATR_{window}", Indicators.Atr(candles, window))],
                Stances.None(candles.Count));
    }
}
