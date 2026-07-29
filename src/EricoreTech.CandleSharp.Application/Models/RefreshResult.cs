namespace EricoreTech.CandleSharp.Application
{
    /// <summary>Outcome of refreshing every saved dataset.</summary>
    public sealed record RefreshResult(
        IReadOnlyList<RefreshedDataset> Refreshed,
        IReadOnlyList<SocialPull> SocialPulls,
        IReadOnlyList<string> Warnings);
}
