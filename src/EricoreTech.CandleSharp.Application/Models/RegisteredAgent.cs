using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>An agent plus where it was loaded from.</summary>
    public sealed record RegisteredAgent(ITradingAgent Agent, string Source);
}
