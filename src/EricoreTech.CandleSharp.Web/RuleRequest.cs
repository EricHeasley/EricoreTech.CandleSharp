namespace EricoreTech.CandleSharp.Web
{
    /// <summary>Body of POST /api/rules. Ticker "*" (or omitted) applies the rule to every stock.</summary>
    internal sealed record RuleRequest(
        string? Name, string? Ticker, string? Action, List<string>? Conditions, int? Horizon, string? Interval);
}
