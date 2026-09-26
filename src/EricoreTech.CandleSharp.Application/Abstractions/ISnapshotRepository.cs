using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Application
{
    /// <summary>
    /// Port for the indicator snapshot journal: per ticker/interval, every bar's
    /// close, indicator values, and stances. Saving merges like candles do: new
    /// bars are appended, shared bars take the fresh values, and bars or
    /// indicators only present in the stored journal are kept.
    /// </summary>
    public interface ISnapshotRepository
    {
        string SnapshotPathFor(string ticker, string interval);

        /// <summary>Returns how many bars were new to the journal.</summary>
        int SaveSnapshots(SnapshotTable table, string interval);

        /// <summary>Null when no journal is stored for the ticker/interval.</summary>
        SnapshotTable? LoadSnapshots(string ticker, string interval);
    }
}
