using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Agents
{
    /// <summary>
    /// The consensus layer ai-hedge-fund-net doesn't have: every loaded
    /// indicator votes with its current stance, weighted equally, plus a read on
    /// which way recent triggers have leaned.
    /// </summary>
    public sealed class ConsensusAgent(int recentTriggerCount = 5) : ITradingAgent
    {
        public string Key => "consensus";
        public string DisplayName => "Consensus";

        public AgentReport Analyze(AgentInput input)
        {
            var (votesCard, bull, bear, total) = AgentScoring.Tally("Indicator votes", input, "");

            var recent = input.Signals.Triggers.TakeLast(recentTriggerCount).ToList();
            int recentBull = recent.Count(t => t.Direction == SignalDirection.Bullish);
            var triggerCard = new ScoreCard(
                "Recent triggers",
                recentBull - (recent.Count - recentBull),
                recent.Count,
                recent.Select(t => $"{t.Timestamp:yyyy-MM-dd} {t.Indicator} fired {t.Direction}").ToList());

            var signal = AgentScoring.BuildSignal(bull, bear, total, "loaded");
            if (recent.Count > 0)
                signal = signal with
                {
                    Reasoning = signal.Reasoning +
                        $" {recentBull} of the last {recent.Count} triggers were bullish.",
                };
            return new AgentReport(Key, DisplayName, input.Ticker, signal, [votesCard, triggerCard]);
        }
    }
}
