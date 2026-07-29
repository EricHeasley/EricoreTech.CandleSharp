namespace EricoreTech.CandleSharp.Application
{
    /// <summary>Outcome of one watch run.</summary>
    public sealed record WatchResult(
        IReadOnlyList<WatchChange> Changes, int TickerCount, IReadOnlyList<string> Warnings);
}
