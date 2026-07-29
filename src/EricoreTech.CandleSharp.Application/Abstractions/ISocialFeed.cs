using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>Port for a social-media message source.</summary>
    public interface ISocialFeed
    {
        Task<List<SocialPost>> FetchPostsAsync(string ticker);
    }
}
