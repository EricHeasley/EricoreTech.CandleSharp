namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>One social-media post about a ticker. Tagged is the author's own Bullish/Bearish label, Neutral when untagged.</summary>
    public sealed record SocialPost(DateTime Timestamp, string Author, string Text, SignalDirection Tagged);
}
