using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Agents
{
    /// <summary>Fades extremes: oscillators and bands. Bullish here means "oversold, expect a bounce".</summary>
    public sealed class MeanReverterAgent : ITradingAgent
    {
        public string Key => "mean_reverter";
        public string DisplayName => "Mean Reverter";

        public AgentReport Analyze(AgentInput input)
        {
            var (oscCard, b1, s1, t1) = AgentScoring.Tally("Oscillators", input, "RSI", "STOCH", "MFI", "CCI");
            var (bandCard, b2, s2, t2) = AgentScoring.Tally("Bands", input, "BB");

            var signal = AgentScoring.BuildSignal(b1 + b2, s1 + s2, t1 + t2, "mean-reversion");
            return new AgentReport(Key, DisplayName, input.Ticker, signal, [oscCard, bandCard]);
        }
    }
}
