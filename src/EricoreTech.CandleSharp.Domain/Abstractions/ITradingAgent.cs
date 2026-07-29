namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// The one contract every trading agent implements. Agents are plugins,
    /// discovered from the same plugins directory as indicators.
    /// </summary>
    public interface ITradingAgent
    {
        /// <summary>Stable snake_case identifier, e.g. "trend_follower".</summary>
        string Key { get; }

        string DisplayName { get; }

        AgentReport Analyze(AgentInput input);
    }
}
