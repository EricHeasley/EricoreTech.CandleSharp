using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>A dataset with the indicator engine's full output over it.</summary>
    public sealed record SeriesAnalysis(
        string Ticker, string Interval,
        IReadOnlyList<Candle> Candles, EngineResult Signals)
    {
        public AgentInput ToAgentInput() => new(Ticker, Interval, Candles, Signals);
    }
}
