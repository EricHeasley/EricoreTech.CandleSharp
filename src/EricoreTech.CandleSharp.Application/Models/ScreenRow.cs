using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>One screener row: a dataset with all directional agent reports, ranked.</summary>
    public sealed record ScreenRow(
        string Ticker, string Interval,
        IReadOnlyList<AgentReport> Reports, double Rank);
}
