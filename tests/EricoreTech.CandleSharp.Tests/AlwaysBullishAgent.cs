using EricoreTech.CandleSharp.Domain;

/// <summary>Known-outcome agent for backtester accounting checks.</summary>
sealed class AlwaysBullishAgent : ITradingAgent
{
    public string Key => "always_bullish";
    public string DisplayName => "Always Bullish";

    public AgentReport Analyze(AgentInput input) =>
        new(Key, DisplayName, input.Ticker,
            new TradeSignal(SignalDirection.Bullish, 100, "always"), []);
}
