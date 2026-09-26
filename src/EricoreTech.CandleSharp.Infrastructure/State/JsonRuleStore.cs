using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using EricoreTech.CandleSharp.Application;
using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Infrastructure
{
    /// <summary>Persists the user's buy/sell rules as data/rules.json (hand-editable).</summary>
    public sealed class JsonRuleStore(string dataDirectory) : IRuleRepository
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            // Keep "<" and ">" readable in the file rather than \u003C escapes.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter() },
        };

        private string FilePath => Path.Combine(dataDirectory, "rules.json");

        public List<TradeRule> LoadRules()
        {
            if (!File.Exists(FilePath)) return [];
            return JsonSerializer.Deserialize<List<TradeRule>>(File.ReadAllText(FilePath), Options) ?? [];
        }

        public void SaveRules(IReadOnlyList<TradeRule> rules)
        {
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(rules, Options));
        }
    }
}
