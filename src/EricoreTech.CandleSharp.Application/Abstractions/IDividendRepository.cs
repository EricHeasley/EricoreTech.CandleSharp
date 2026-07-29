using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>Port for dividend persistence. Saving merges with existing history.</summary>
    public interface IDividendRepository
    {
        string SaveDividends(IReadOnlyList<Dividend> dividends, string ticker);

        /// <summary>Empty list when no dividend data is stored for the ticker.</summary>
        List<Dividend> LoadDividends(string ticker);
    }
}
