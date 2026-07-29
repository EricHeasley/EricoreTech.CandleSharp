using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>Port for candle persistence. Saving merges with existing history.</summary>
    public interface ICandleRepository
    {
        string DataDirectory { get; }

        string Save(IReadOnlyList<Candle> candles, string ticker, string interval);

        List<Candle> Load(string ticker, string interval);

        IReadOnlyList<DatasetInfo> List();
    }
}
