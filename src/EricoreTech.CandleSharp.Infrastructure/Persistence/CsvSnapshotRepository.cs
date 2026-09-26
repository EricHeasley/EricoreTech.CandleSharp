using System.Globalization;
using EricoreTech.CandleSharp.Application;
using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Infrastructure
{
    /// <summary>
    /// Indicator snapshot journal as CSV: data/snapshots/AAPL_1d.csv with one row
    /// per bar — Date, Close, every indicator column, then one "stance:NAME"
    /// column per indicator. Kept in its own folder so it never shows up as a
    /// candle dataset. Merging mirrors the candle store: on a shared bar the new
    /// values win; bars and columns only on record are kept.
    /// </summary>
    public sealed class CsvSnapshotRepository(string dataDir = "data") : ISnapshotRepository
    {
        private const string DateFormat = "yyyy-MM-ddTHH:mm:ss";
        private const string StancePrefix = "stance:";

        public string SnapshotPathFor(string ticker, string interval) =>
            Path.Combine(dataDir, "snapshots", $"{ticker.ToUpperInvariant()}_{interval}.csv");

        public int SaveSnapshots(SnapshotTable table, string interval)
        {
            var path = SnapshotPathFor(table.Ticker, interval);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var journal = LoadSnapshots(table.Ticker, interval);
            var columns = journal?.Columns.Select(c => c.Name).ToList() ?? [];
            var stances = journal?.Stances.Keys.ToList() ?? [];
            var rows = new SortedDictionary<DateTime, Row>();
            if (journal is not null)
                for (int i = 0; i < journal.Count; i++)
                    rows[journal.Timestamps[i]] = new Row(journal.Closes[i],
                        journal.Columns.ToDictionary(c => c.Name, c => c.Values[i]),
                        journal.Stances.ToDictionary(s => s.Key, s => (SignalDirection?)s.Value[i]));

            foreach (var column in table.Columns)
                if (!columns.Contains(column.Name)) columns.Add(column.Name);
            foreach (var name in table.Stances.Keys)
                if (!stances.Contains(name)) stances.Add(name);

            int added = 0;
            for (int i = 0; i < table.Count; i++)
            {
                // Prices get back-adjusted for splits and dividends, so the fresh
                // computation replaces a stored bar rather than mixing price scales.
                if (!rows.TryGetValue(table.Timestamps[i], out var previous)) added++;
                var row = new Row(table.Closes[i],
                    previous?.Values ?? [], previous?.Stances ?? []);
                rows[table.Timestamps[i]] = row;
                foreach (var column in table.Columns)
                    row.Values[column.Name] = column.Values[i];
                foreach (var (name, stance) in table.Stances)
                    row.Stances[name] = stance[i];
            }

            using var writer = new StreamWriter(path);
            writer.WriteLine(string.Join(',',
                new[] { "Date", "Close" }.Concat(columns).Concat(stances.Select(s => StancePrefix + s))));
            foreach (var (timestamp, row) in rows)
            {
                var cells = new List<string> { timestamp.ToString(DateFormat, CultureInfo.InvariantCulture), Num(row.Close) };
                cells.AddRange(columns.Select(c => row.Values.GetValueOrDefault(c) is { } v ? Num(v) : ""));
                cells.AddRange(stances.Select(s => row.Stances.GetValueOrDefault(s)?.ToString() ?? ""));
                writer.WriteLine(string.Join(',', cells));
            }
            return added;
        }

        public SnapshotTable? LoadSnapshots(string ticker, string interval)
        {
            var path = SnapshotPathFor(ticker, interval);
            if (!File.Exists(path)) return null;

            var lines = File.ReadLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (lines.Count == 0) return null;
            var header = lines[0].Split(',');
            var body = lines.Skip(1).Select(l => l.Split(',')).ToList();

            var timestamps = body.Select(f => DateTime.ParseExact(f[0], DateFormat, CultureInfo.InvariantCulture)).ToList();
            var closes = body.Select(f => double.Parse(f[1], CultureInfo.InvariantCulture)).ToList();
            var columns = new List<(string Name, double?[] Values)>();
            var stances = new Dictionary<string, SignalDirection[]>();
            for (int c = 2; c < header.Length; c++)
            {
                int col = c;
                if (header[c].StartsWith(StancePrefix, StringComparison.Ordinal))
                    stances[header[c][StancePrefix.Length..]] = body
                        .Select(f => col < f.Length && Enum.TryParse<SignalDirection>(f[col], out var d) ? d : SignalDirection.Neutral)
                        .ToArray();
                else
                    columns.Add((header[c], body
                        .Select(f => col < f.Length && f[col].Length > 0
                            ? double.Parse(f[col], CultureInfo.InvariantCulture)
                            : (double?)null)
                        .ToArray()));
            }
            return new SnapshotTable(ticker.ToUpperInvariant(), timestamps, closes, columns, stances);
        }

        private static string Num(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        private sealed record Row(
            double Close, Dictionary<string, double?> Values, Dictionary<string, SignalDirection?> Stances);
    }
}
