namespace EricoreTech.CandleSharp.Application
{
    /// <summary>Port for persisting the last-seen agent verdicts (watch mode), keyed ticker -> agent -> verdict.</summary>
    public interface IAgentStateStore
    {
        Dictionary<string, Dictionary<string, string>> Load(string interval);

        void Save(string interval, Dictionary<string, Dictionary<string, string>> verdicts);
    }
}
