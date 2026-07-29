namespace EricoreTech.CandleSharp.Application
{
    /// <summary>Chatter pulled for one ticker during a refresh-all run.</summary>
    public sealed record SocialPull(string Ticker, int Posts);
}
