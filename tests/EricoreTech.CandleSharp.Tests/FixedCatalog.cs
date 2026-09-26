using EricoreTech.CandleSharp.Application;
using EricoreTech.CandleSharp.Domain;

/// <summary>Plugin catalog with a fixed indicator set, so tests don't depend on built plugin DLLs.</summary>
sealed class FixedCatalog(IReadOnlyList<ITechnicalIndicator> indicators) : IPluginCatalog
{
    public IReadOnlyList<RegisteredIndicator> Indicators { get; } =
        indicators.Select(i => new RegisteredIndicator(i, "test")).ToList();

    public IReadOnlyList<RegisteredAgent> Agents { get; } = [];

    public IReadOnlyList<string> ScannedDirectories { get; } = [];

    public IndicatorEngine CreateEngine() => new(indicators);
}
