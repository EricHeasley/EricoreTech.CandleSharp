namespace EricoreTech.CandleSharp.Domain
{
    // The trading-agent layer, modeled on ai-hedge-fund-net's IAgent contract:
    // an agent takes prepared data (it never fetches), scores it with
    // deterministic rules into scorecards, and emits one TradeSignal with
    // direction, 0-100 confidence, and human-readable reasoning. Reasoning is
    // generated from the scorecards in code today; the contract leaves room to
    // swap in an LLM narrator later without touching agents or hosts.

    /// <summary>Everything an agent gets to work with: candles plus the engine's full signal output.</summary>
    public sealed record AgentInput(
        string Ticker,
        string Interval,
        IReadOnlyList<Candle> Candles,
        EngineResult Signals,
        SocialSentimentSummary? Social = null);
}
