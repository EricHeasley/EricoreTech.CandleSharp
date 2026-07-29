using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>Port for social-post persistence. Saving merges with stored posts.</summary>
    public interface ISocialRepository
    {
        void SavePosts(IReadOnlyList<SocialPost> posts, string ticker);

        /// <summary>Empty list when nothing is stored for the ticker.</summary>
        List<SocialPost> LoadPosts(string ticker);
    }
}
