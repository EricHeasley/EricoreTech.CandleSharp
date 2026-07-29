using System.Text.Json;
using EricoreTech.CandleSharp.Application;

namespace EricoreTech.CandleSharp.Infrastructure
{
    /// <summary>Persists watch-mode verdict state as JSON next to the candle data.</summary>
    public sealed class JsonAgentStateStore(string dataDirectory) : IAgentStateStore
    {
        private string PathFor(string interval) =>
            Path.Combine(dataDirectory, $".agent-state.{interval}.json");

        public Dictionary<string, Dictionary<string, string>> Load(string interval)
        {
            var path = PathFor(interval);
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
                File.ReadAllText(path)) ?? [];
        }

        public void Save(string interval, Dictionary<string, Dictionary<string, string>> verdicts)
        {
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(PathFor(interval), JsonSerializer.Serialize(verdicts,
                new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
