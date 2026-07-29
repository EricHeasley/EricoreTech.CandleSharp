using EricoreTech.CandleSharp.Domain;

/// <summary>Custom indicator with a scripted stance — proves any ITechnicalIndicator plugs into the engine.</summary>
sealed class StubIndicator(string name, SignalDirection[] stance) : ITechnicalIndicator
{
    public string Name => name;

    public IndicatorResult Compute(IReadOnlyList<Candle> candles) =>
        new([("STUB_value", new double?[candles.Count])], stance);
}
