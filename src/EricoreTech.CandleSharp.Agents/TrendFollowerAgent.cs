using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Agents
{
    /// <summary>Follows the trend: moving-average crosses, trailing stops, trend strength, the cloud.</summary>
    public sealed class TrendFollowerAgent : ITradingAgent
    {
        public string Key => "trend_follower";
        public string DisplayName => "Trend Follower";

        public AgentReport Analyze(AgentInput input)
        {
            var (maCard, b1, s1, t1) = AgentScoring.Tally("Moving averages", input, "SMA_cross", "EMA_cross", "MACD");
            var (stopCard, b2, s2, t2) = AgentScoring.Tally("Trailing stops", input, "PSAR", "SUPERTREND");
            var (strengthCard, b3, s3, t3) = AgentScoring.Tally("Trend strength & cloud", input, "ADX", "ICHIMOKU");

            var signal = AgentScoring.BuildSignal(b1 + b2 + b3, s1 + s2 + s3, t1 + t2 + t3, "trend");
            return new AgentReport(Key, DisplayName, input.Ticker, signal, [maCard, stopCard, strengthCard]);
        }
    }
}
