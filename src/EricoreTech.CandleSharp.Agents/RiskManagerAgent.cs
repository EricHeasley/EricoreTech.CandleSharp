using System.Globalization;
using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Agents
{
    /// <summary>
    /// Non-directional position-sizing advisor (ai-hedge-fund-net's risk-cap
    /// idea): a fixed account risk per trade with an ATR-multiple stop, capped
    /// at a maximum share of the account.
    /// </summary>
    public sealed class RiskManagerAgent(
        double accountValue = 10_000,
        double riskPercent = 1.0,
        double atrMultiple = 2.0,
        double maxPositionPercent = 20) : ITradingAgent
    {
        public string Key => "risk_manager";
        public string DisplayName => "Risk Manager";

        public AgentReport Analyze(AgentInput input)
        {
            double? atr = SeriesLookup.LastValue(SeriesLookup.Column(input, "ATR_"));
            if (atr is not { } a || a <= 0 || input.Candles.Count == 0)
                return new AgentReport(Key, DisplayName, input.Ticker,
                    new TradeSignal(SignalDirection.Neutral, 0, "Position sizing requires the ATR indicator and price data."),
                    []);

            double close = input.Candles[^1].Close;
            double riskDollars = accountValue * riskPercent / 100;
            double stopDistance = atrMultiple * a;
            int shares = (int)Math.Floor(riskDollars / stopDistance);
            double cap = accountValue * maxPositionPercent / 100;
            bool capped = shares * close > cap;
            if (capped) shares = (int)Math.Floor(cap / close);
            double positionValue = shares * close;

            var details = new List<string>
            {
                string.Create(CultureInfo.InvariantCulture, $"Last close {close:0.00}, ATR {a:0.00}"),
                string.Create(CultureInfo.InvariantCulture, $"Stop distance {atrMultiple}xATR = {stopDistance:0.00} (long stop {close - stopDistance:0.00}, short stop {close + stopDistance:0.00})"),
                string.Create(CultureInfo.InvariantCulture, $"Risk budget {riskPercent}% of {accountValue:0} = {riskDollars:0.00} per trade"),
                string.Create(CultureInfo.InvariantCulture, $"Suggested size: {shares} shares (~{positionValue:0})")
                    + (capped ? string.Create(CultureInfo.InvariantCulture, $" — capped at {maxPositionPercent}% of account") : ""),
            };
            string reasoning = string.Create(CultureInfo.InvariantCulture,
                $"Risking {riskPercent}% (~{riskDollars:0}) with a {atrMultiple}xATR stop supports {shares} shares (~{positionValue:0}).");
            if (capped)
                reasoning += string.Create(CultureInfo.InvariantCulture,
                    $" Size capped at {maxPositionPercent}% of the account.");

            return new AgentReport(Key, DisplayName, input.Ticker,
                new TradeSignal(SignalDirection.Neutral, 0, reasoning),
                [new ScoreCard("Position sizing", shares, shares, details)]);
        }
    }
}
