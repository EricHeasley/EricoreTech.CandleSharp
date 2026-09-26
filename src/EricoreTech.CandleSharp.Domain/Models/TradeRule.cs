namespace EricoreTech.CandleSharp.Domain
{
    public enum RuleAction
    {
        Buy,
        Sell,
    }

    /// <summary>
    /// A user-defined rule: when every condition holds on a bar, the rule says
    /// Buy (or Sell). Ticker "*" applies the rule to every stock. Conditions are
    /// kept as text in the <see cref="RuleCondition"/> syntax, e.g.
    /// "RSI_14 &lt; 30", "MACD_12_26_9 turns Bullish", "Close crosses above SMA_50".
    /// Horizon is how many bars ahead past signals are scored.
    /// </summary>
    public sealed record TradeRule(
        string Id,
        string Name,
        string Ticker,
        RuleAction Action,
        IReadOnlyList<string> Conditions,
        int Horizon = 10)
    {
        public const string AnyTicker = "*";

        public bool AppliesTo(string ticker) =>
            Ticker == AnyTicker || string.Equals(Ticker, ticker, StringComparison.OrdinalIgnoreCase);
    }
}
