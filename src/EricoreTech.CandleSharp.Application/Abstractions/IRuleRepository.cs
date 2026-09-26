using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>Port for the user's saved buy/sell rules.</summary>
    public interface IRuleRepository
    {
        /// <summary>Empty list when no rules are saved yet.</summary>
        List<TradeRule> LoadRules();

        void SaveRules(IReadOnlyList<TradeRule> rules);
    }
}
