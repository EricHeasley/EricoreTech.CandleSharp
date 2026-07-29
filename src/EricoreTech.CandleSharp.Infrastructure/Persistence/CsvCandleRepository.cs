using System.Globalization;
using EricoreTech.CandleSharp.Application;
using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Infrastructure
{
    /// <summary>
    /// Local CSV storage. Each ticker/interval pair gets one file: data/AAPL_1d.csv.
    /// Saving merges with any existing file so repeated fetches extend history
    /// instead of clobbering it; on duplicate timestamps the new bar wins.
    /// </summary>
    public sealed class CsvCandleRepository(string dataDir = "data") : ICandleRepository, IDividendRepository
    {
        private const string Header = "Date,Open,High,Low,Close,Volume";
        private const string DateFormat = "yyyy-MM-ddTHH:mm:ss";

        public string DataDirectory { get; } = dataDir;

        public string PathFor(string ticker, string interval) =>
            Path.Combine(DataDirectory, $"{ticker.ToUpperInvariant()}_{interval}.csv");

        public string Save(IReadOnlyList<Candle> candles, string ticker, string interval)
        {
            Directory.CreateDirectory(DataDirectory);
            var path = PathFor(ticker, interval);

            var byTime = new SortedDictionary<DateTime, Candle>();
            if (File.Exists(path))
                foreach (var c in Load(ticker, interval))
                    byTime[c.Timestamp] = c;
            foreach (var c in candles)
                byTime[c.Timestamp] = c;

            WriteCsv(path, byTime.Values);
            return path;
        }

        public List<Candle> Load(string ticker, string interval)
        {
            var path = PathFor(ticker, interval);
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"No local data for {ticker} at {path}. Fetch it first.", path);

            var candles = new List<Candle>();
            foreach (var line in File.ReadLines(path).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var f = line.Split(',');
                candles.Add(new Candle(
                    DateTime.ParseExact(f[0], DateFormat, CultureInfo.InvariantCulture),
                    double.Parse(f[1], CultureInfo.InvariantCulture),
                    double.Parse(f[2], CultureInfo.InvariantCulture),
                    double.Parse(f[3], CultureInfo.InvariantCulture),
                    double.Parse(f[4], CultureInfo.InvariantCulture),
                    long.Parse(f[5], CultureInfo.InvariantCulture)));
            }
            return candles;
        }

        public IReadOnlyList<DatasetInfo> List()
        {
            var datasets = new List<DatasetInfo>();
            if (!Directory.Exists(DataDirectory)) return datasets;
            foreach (var path in Directory.GetFiles(DataDirectory, "*.csv").Order())
            {
                var stem = Path.GetFileNameWithoutExtension(path);
                if (stem.EndsWith("_indicators") || stem.EndsWith("_dividends")) continue;
                int split = stem.LastIndexOf('_');
                if (split <= 0) continue;
                datasets.Add(new DatasetInfo(stem[..split], stem[(split + 1)..], path));
            }
            return datasets;
        }

        public string SaveDividends(IReadOnlyList<Dividend> dividends, string ticker)
        {
            Directory.CreateDirectory(DataDirectory);
            var path = DividendPathFor(ticker);

            var byDate = new SortedDictionary<DateTime, Dividend>();
            foreach (var d in LoadDividends(ticker)) byDate[d.Timestamp] = d;
            foreach (var d in dividends) byDate[d.Timestamp] = d;

            using var writer = new StreamWriter(path);
            writer.WriteLine("Date,Amount");
            foreach (var d in byDate.Values)
                writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{d.Timestamp:yyyy-MM-dd},{d.Amount:0.######}"));
            return path;
        }

        public List<Dividend> LoadDividends(string ticker)
        {
            var path = DividendPathFor(ticker);
            var dividends = new List<Dividend>();
            if (!File.Exists(path)) return dividends;
            foreach (var line in File.ReadLines(path).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var f = line.Split(',');
                dividends.Add(new Dividend(
                    DateTime.ParseExact(f[0], "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    double.Parse(f[1], CultureInfo.InvariantCulture)));
            }
            return dividends;
        }

        private string DividendPathFor(string ticker) =>
            Path.Combine(DataDirectory, $"{ticker.ToUpperInvariant()}_dividends.csv");

        private static void WriteCsv(string path, IEnumerable<Candle> candles)
        {
            using var writer = new StreamWriter(path);
            writer.WriteLine(Header);
            foreach (var c in candles)
                writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{c.Timestamp.ToString(DateFormat)},{c.Open:0.######},{c.High:0.######},{c.Low:0.######},{c.Close:0.######},{c.Volume}"));
        }
    }
}
