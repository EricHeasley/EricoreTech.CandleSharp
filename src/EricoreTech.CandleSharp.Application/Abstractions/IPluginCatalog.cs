using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>Port for indicator/agent plugin discovery.</summary>
    public interface IPluginCatalog
    {
        IReadOnlyList<RegisteredIndicator> Indicators { get; }

        IReadOnlyList<RegisteredAgent> Agents { get; }

        /// <summary>The directories that were (or will be) scanned, for diagnostics.</summary>
        IReadOnlyList<string> ScannedDirectories { get; }

        IndicatorEngine CreateEngine();
    }
}
