using EricoreTech.CandleSharp.Application;
using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Infrastructure
{
    /// <summary>
    /// Discovers indicator and agent plugins from DLLs. Scans the requested
    /// directory (cwd-relative by default) plus, as a fallback for published
    /// apps, a "plugins" folder next to the executable. Duplicate names are
    /// deduped with later-loaded winning, so a user plugin can override a
    /// shipped indicator or agent by reusing its name.
    /// </summary>
    public sealed class PluginCatalog : IPluginCatalog
    {
        private readonly Lazy<(List<RegisteredIndicator> Indicators, List<RegisteredAgent> Agents)> _loaded;

        public PluginCatalog(string pluginsDirectory = "plugins", Action<string>? onError = null)
        {
            var directories = new List<string> { pluginsDirectory };
            var appLocal = Path.Combine(AppContext.BaseDirectory, "plugins");
            if (Path.GetFullPath(pluginsDirectory) != Path.GetFullPath(appLocal))
                directories.Add(appLocal);
            ScannedDirectories = directories;
            _loaded = new(() => Load(onError));
        }

        public IReadOnlyList<string> ScannedDirectories { get; }

        public IReadOnlyList<RegisteredIndicator> Indicators => _loaded.Value.Indicators;

        public IReadOnlyList<RegisteredAgent> Agents => _loaded.Value.Agents;

        public IndicatorEngine CreateEngine() => new(Indicators.Select(r => r.Indicator));

        private (List<RegisteredIndicator>, List<RegisteredAgent>) Load(Action<string>? onError)
        {
            var indicators = new List<RegisteredIndicator>();
            var agents = new List<RegisteredAgent>();
            foreach (var directory in ScannedDirectories)
            {
                foreach (var (indicator, source) in PluginLoader.LoadFrom<ITechnicalIndicator>(directory, onError))
                    Register(indicators, new RegisteredIndicator(indicator, source), r => r.Indicator.Name);
                foreach (var (agent, source) in PluginLoader.LoadFrom<ITradingAgent>(directory, onError))
                    Register(agents, new RegisteredAgent(agent, source), r => r.Agent.Key);
            }
            return (indicators, agents);
        }

        private static void Register<T>(List<T> registered, T entry, Func<T, string> keyOf)
        {
            int existing = registered.FindIndex(r => keyOf(r) == keyOf(entry));
            if (existing >= 0) registered[existing] = entry;
            else registered.Add(entry);
        }
    }
}
