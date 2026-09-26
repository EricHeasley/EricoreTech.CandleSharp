namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// The bar-aligned series a rule can read: Open/High/Low/Close/Volume plus
    /// every indicator column and stance from an engine run. Names are
    /// case-insensitive.
    /// </summary>
    public sealed class RuleData
    {
        private readonly Dictionary<string, double?[]> _values = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SignalDirection[]> _stances = new(StringComparer.OrdinalIgnoreCase);

        public RuleData(IReadOnlyList<Candle> candles, EngineResult signals)
        {
            Candles = candles;
            _values["Open"] = candles.Select(c => (double?)c.Open).ToArray();
            _values["High"] = candles.Select(c => (double?)c.High).ToArray();
            _values["Low"] = candles.Select(c => (double?)c.Low).ToArray();
            _values["Close"] = candles.Select(c => (double?)c.Close).ToArray();
            _values["Volume"] = candles.Select(c => (double?)c.Volume).ToArray();
            foreach (var (name, values) in signals.Columns)
                _values.TryAdd(name, values);
            foreach (var (name, stance) in signals.Stances)
                _stances[name] = stance;
        }

        public IReadOnlyList<Candle> Candles { get; }

        public int Count => Candles.Count;

        public IEnumerable<string> ValueNames => _values.Keys;

        public IEnumerable<string> StanceNames => _stances.Keys;

        public bool HasValue(string name) => _values.ContainsKey(name);

        public bool HasStance(string name) => _stances.ContainsKey(name);

        public double?[] Values(string name) => _values.TryGetValue(name, out var v)
            ? v
            : throw new InvalidOperationException($"no value named \"{name}\" (try Close, RSI_14, SMA_50, MACD ...)");

        public SignalDirection[] Stance(string name) => _stances.TryGetValue(name, out var s)
            ? s
            : throw new InvalidOperationException($"no indicator stance named \"{name}\" (try RSI_14, MACD_12_26_9, SMA_cross_20_50 ...)");
    }
}
