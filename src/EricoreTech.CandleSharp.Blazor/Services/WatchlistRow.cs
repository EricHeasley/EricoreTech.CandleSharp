namespace EricoreTech.CandleSharp.Blazor.Services
{
    /// <summary>One sidebar entry: a saved dataset plus the at-a-glance numbers the watchlist shows.</summary>
    public sealed record WatchlistRow(
        string Ticker,
        string Interval,
        int Bars,
        double LastClose,
        double ChangePercent,
        IReadOnlyList<double> Sparkline,
        int RulesFiring,
        string? ConsensusDirection);
}
