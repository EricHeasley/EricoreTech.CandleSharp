// Offline smoke tests for storage and Indicators, using synthetic data.
// Deliberately framework-free so the suite builds without NuGet access:
//   dotnet run --project tests/EricoreTech.CandleSharp.Tests
using EricoreTech.CandleSharp.Advanced;
using EricoreTech.CandleSharp.Application;
using EricoreTech.CandleSharp.Domain;
using EricoreTech.CandleSharp.Infrastructure;
using EricoreTech.CandleSharp.Standard;

int failures = 0;

var candles = MakeSyntheticCandles(250);

// --- CsvCandleRepository: save, merge-on-overlap, load round-trip ---
var dataDir = Path.Combine(Path.GetTempPath(), $"candlesharp-tests-{Guid.NewGuid():N}");
try
{
    var storage = new CsvCandleRepository(dataDir);
    storage.Save(candles.Take(200).ToList(), "TEST", "1d");
    storage.Save(candles.Skip(190).ToList(), "TEST", "1d");
    var loaded = storage.Load("TEST", "1d");

    Check(loaded.Count == 250, $"merge produced {loaded.Count} bars, expected 250");
    Check(loaded.Select(c => c.Timestamp).Distinct().Count() == 250, "duplicate timestamps after merge");
    Check(loaded.SequenceEqual(loaded.OrderBy(c => c.Timestamp)), "bars not sorted after merge");
    Check(Math.Abs(loaded[100].Close - candles[100].Close) < 1e-6, "close mangled by CSV round-trip");

    var listed = storage.List();
    Check(listed.Count == 1 && listed[0].Ticker == "TEST" && listed[0].Interval == "1d",
        $"List returned {listed.Count} entries");
}
finally
{
    if (Directory.Exists(dataDir)) Directory.Delete(dataDir, recursive: true);
}

// --- Indicators: warm-up nulls, ranges, internal consistency ---
var sma20 = Indicators.Sma(candles, 20);
Check(sma20[18] is null && sma20[19] is not null, "SMA_20 warm-up boundary wrong");
double expectedSma = candles.Skip(230).Take(20).Average(c => c.Close);
Check(Math.Abs(sma20[249]!.Value - expectedSma) < 1e-9, "SMA_20 value mismatch");

var ema12 = Indicators.Ema(candles, 12);
Check(ema12[0]!.Value == candles[0].Close, "EMA not seeded with first close");

var rsi = Indicators.Rsi(candles);
Check(rsi[0] is null, "RSI should be null at index 0");
Check(rsi.Skip(1).All(v => v is >= 0 and <= 100), "RSI out of [0, 100]");

var (macd, signal, hist) = Indicators.Macd(candles);
for (int i = 0; i < candles.Count; i++)
    Check(Math.Abs(hist[i]!.Value - (macd[i]!.Value - signal[i]!.Value)) < 1e-9,
        $"MACD histogram != macd - signal at {i}");

var (mid, upper, lower) = Indicators.BollingerBands(candles);
for (int i = 19; i < candles.Count; i++)
{
    Check(upper[i]!.Value >= mid[i]!.Value && mid[i]!.Value >= lower[i]!.Value,
        $"Bollinger bands out of order at {i}");
}

var atr = Indicators.Atr(candles);
Check(atr.All(v => v is >= 0), "ATR negative");

// A constant series: RSI has no losses -> 100 by convention; bands collapse onto the SMA.
var flat = Enumerable.Range(0, 60)
    .Select(i => new Candle(new DateTime(2024, 1, 1).AddDays(i), 50, 50, 50, 50, 1000))
    .ToList();
Check(Indicators.Rsi(flat)[59] == 100, "RSI of flat series should be 100");
var (fMid, fUpper, fLower) = Indicators.BollingerBands(flat);
Check(Math.Abs(fUpper[59]!.Value - fLower[59]!.Value) < 1e-9, "flat-series bands should collapse");

// --- IndicatorEngine: uniform trigger rule, verified with a scripted stance ---
// Stance sequence: N N Bull Bull Bear N N Bull. Uniform rule says triggers fire
// exactly where the stance CHANGES to a directional value: bars 2, 4, and 7.
// Bar 3 (unchanged), bar 5 (change to Neutral): no trigger.
var scripted = new SignalDirection[]
{
    SignalDirection.Neutral, SignalDirection.Neutral,
    SignalDirection.Bullish, SignalDirection.Bullish,
    SignalDirection.Bearish,
    SignalDirection.Neutral, SignalDirection.Neutral,
    SignalDirection.Bullish,
};
var stubCandles = candles.Take(scripted.Length).ToList();
var engineResult = new IndicatorEngine([new StubIndicator("STUB", scripted)]).Run(stubCandles);

Check(engineResult.Triggers.Count == 3,
    $"scripted stance produced {engineResult.Triggers.Count} triggers, expected 3");
Check(engineResult.Triggers[0].Timestamp == stubCandles[2].Timestamp
    && engineResult.Triggers[0].Direction == SignalDirection.Bullish, "first trigger wrong");
Check(engineResult.Triggers[1].Timestamp == stubCandles[4].Timestamp
    && engineResult.Triggers[1].Direction == SignalDirection.Bearish, "second trigger wrong");
Check(engineResult.Triggers[2].Timestamp == stubCandles[7].Timestamp
    && engineResult.Triggers[2].Direction == SignalDirection.Bullish, "third trigger wrong");
Check(engineResult.Triggers.All(t => t.Indicator == "STUB"), "trigger indicator name wrong");

// --- Advanced indicators: ranges, warm-ups, invariants ---
var (stochK, stochD) = Indicators.Stochastic(candles);
Check(stochK.Where(v => v is not null).All(v => v is >= 0 and <= 100), "Stochastic %K out of [0,100]");
Check(stochK[14] is null && stochK[15] is not null, "Stochastic %K warm-up boundary wrong (13 + smooth-1)");
Check(stochD[17] is not null, "Stochastic %D missing after warm-up");
var (flatK, _) = Indicators.Stochastic(flat);
Check(flatK[59] == 50, "flat-series stochastic should be 50");

var (adxVals, plusDi, minusDi) = Indicators.Adx(candles);
Check(adxVals.Where(v => v is not null).All(v => v is >= 0 and <= 100), "ADX out of [0,100]");
Check(plusDi.Where(v => v is not null).All(v => v >= 0), "+DI negative");

var rising = MakeTrendingCandles(120, drift: +1.0);
var (risingAdx, risingPlus, risingMinus) = Indicators.Adx(rising);
Check(risingPlus[119]!.Value > risingMinus[119]!.Value, "+DI should dominate in an uptrend");
Check(risingAdx[119]!.Value > 25, "ADX should read a strong trend on a steady climb");

var (tenkan, kijun, senkouA, senkouB, chikou) = Indicators.Ichimoku(candles);
Check(tenkan[7] is null && tenkan[8] is not null, "Tenkan warm-up wrong");
Check(senkouA[50] is null && senkouA[51] is not null, "Senkou A shift wrong (kijun warm-up 25 + shift 26)");
Check(senkouB[76] is null && senkouB[77] is not null, "Senkou B shift wrong (51 + 26)");
Check(chikou[0] == candles[26].Close && chikou[249] is null, "Chikou back-shift wrong");

var sar = Indicators.ParabolicSar(candles);
Check(sar[0] is null && sar.Skip(1).All(v => v is not null), "PSAR should exist from bar 1");
var risingSar = Indicators.ParabolicSar(rising);
Check(risingSar[119]!.Value < rising[119].Close, "PSAR should trail below price in an uptrend");

var (stLine, stDir) = Indicators.SuperTrend(candles);
for (int i = 10; i < candles.Count; i++)
    Check(stDir[i] == 1 ? candles[i].Close >= stLine[i]!.Value : candles[i].Close <= stLine[i]!.Value,
        $"SuperTrend line on wrong side of price at {i}");

var mfi = Indicators.Mfi(candles);
Check(mfi[13] is null && mfi[14] is not null, "MFI warm-up wrong");
Check(mfi.Where(v => v is not null).All(v => v is >= 0 and <= 100), "MFI out of [0,100]");

var cci = Indicators.Cci(candles);
Check(cci[18] is null && cci[19] is not null, "CCI warm-up wrong");
Check(Indicators.Cci(flat)[59] == 0, "flat-series CCI should be 0");

var (dcUpper, dcMid, dcLower) = Indicators.Donchian(candles);
for (int i = 19; i < candles.Count; i++)
    Check(dcUpper[i]!.Value >= dcMid[i]!.Value && dcMid[i]!.Value >= dcLower[i]!.Value,
        $"Donchian channel out of order at {i}");
var donchianStance = new DonchianIndicator(20).Compute(rising).Stance;
Check(!donchianStance.Contains(SignalDirection.Bearish), "Donchian should never read Bearish on a steady climb");
Check(donchianStance.Contains(SignalDirection.Bullish), "Donchian should break out Bullish on a steady climb");

// --- Resampler: daily -> weekly/monthly buckets ---
// Mon Jan 1 2024 .. Fri Jan 12: two full ISO weeks of business days.
var twoWeeks = MakeTrendingCandles(10, drift: +1.0);
var weekly = Resampler.Aggregate(twoWeeks.Where(c => c.Timestamp.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList(), AggregatePeriod.Weekly);
Check(weekly.Count == 2, $"expected 2 weekly buckets, got {weekly.Count}");
Check(weekly[0].Timestamp == new DateTime(2024, 1, 1) && weekly[1].Timestamp == new DateTime(2024, 1, 8),
    "weekly buckets not stamped with ISO Monday");
var firstWeekDays = twoWeeks.Where(c => c.Timestamp < new DateTime(2024, 1, 6)).ToList();
Check(weekly[0].Open == firstWeekDays[0].Open && weekly[0].Close == firstWeekDays[^1].Close,
    "weekly open/close should be first open / last close");
Check(weekly[0].High == firstWeekDays.Max(c => c.High) && weekly[0].Low == firstWeekDays.Min(c => c.Low),
    "weekly high/low should be extremes of the week");
Check(weekly[0].Volume == firstWeekDays.Sum(c => c.Volume), "weekly volume should sum the week");
var monthly = Resampler.Aggregate(candles, AggregatePeriod.Monthly);
Check(monthly.Count == candles.Select(c => (c.Timestamp.Year, c.Timestamp.Month)).Distinct().Count(),
    "monthly bucket count wrong");
Check(monthly.All(c => c.Timestamp.Day == 1), "monthly buckets not stamped with the 1st");
Check(Resampler.FromInterval("1wk") == AggregatePeriod.Weekly
    && Resampler.FromInterval("1mo") == AggregatePeriod.Monthly
    && Resampler.FromInterval("1d") is null, "FromInterval mapping wrong");

// --- Parameter validation: bad lookbacks throw instead of computing garbage ---
Check(Throws(() => Indicators.Sma(candles, 0)), "Sma(0) should throw");
Check(Throws(() => Indicators.BollingerBands(candles, 1)), "Bollinger window 1 should throw");
Check(Throws(() => Indicators.Macd(candles, fast: 26, slow: 12)), "MACD fast >= slow should throw");
Check(Throws(() => Indicators.ParabolicSar(candles, step: 0.3, maxStep: 0.2)), "PSAR step > maxStep should throw");
Check(!Throws(() => Indicators.Sma(candles, 1)), "Sma(1) is valid and should not throw");

// --- Candle patterns: hand-built shapes must match ---
var day = new DateTime(2024, 6, 3);
var engulfPair = new List<Candle>
{
    new(day, Open: 10.0, High: 10.1, Low: 8.9, Close: 9.0, Volume: 1000),          // down bar
    new(day.AddDays(1), Open: 8.8, High: 10.4, Low: 8.7, Close: 10.2, Volume: 1000), // engulfs it upward
};
var engulf = new EricoreTech.CandleSharp.Patterns.EngulfingIndicator().Compute(engulfPair);
Check(engulf.Stance[1] == SignalDirection.Bullish && engulf.Columns[0].Values[1] == 1,
    "bullish engulfing not detected");

var hammerBar = new List<Candle> { new(day, Open: 10.0, High: 10.25, Low: 8.0, Close: 10.2, Volume: 1000) };
var hammer = new EricoreTech.CandleSharp.Patterns.HammerIndicator().Compute(hammerBar);
Check(hammer.Stance[0] == SignalDirection.Bullish, "hammer not detected");
var starBar = new List<Candle> { new(day, Open: 10.0, High: 12.0, Low: 9.95, Close: 9.98, Volume: 1000) };
Check(new EricoreTech.CandleSharp.Patterns.HammerIndicator().Compute(starBar).Stance[0] == SignalDirection.Bearish,
    "shooting star not detected");

var maruBar = new List<Candle> { new(day, Open: 10.0, High: 11.01, Low: 9.99, Close: 11.0, Volume: 1000) };
Check(new EricoreTech.CandleSharp.Patterns.MarubozuIndicator().Compute(maruBar).Stance[0] == SignalDirection.Bullish,
    "bullish marubozu not detected");
var flatBar = new List<Candle> { new(day, Open: 10.0, High: 10.6, Low: 9.4, Close: 10.1, Volume: 1000) };
Check(new EricoreTech.CandleSharp.Patterns.MarubozuIndicator().Compute(flatBar).Stance[0] == SignalDirection.Neutral,
    "ordinary bar should not be a marubozu");

// --- Full plugin-loaded set through the engine (both packs + OBV) ---
var testCatalog = new PluginCatalog(AppContext.BaseDirectory);
var fullSet = testCatalog.Indicators;
var full = testCatalog.CreateEngine().Run(candles);
string[] expectedColumns =
[
    "SMA_20", "SMA_50", "EMA_12", "EMA_26", "RSI_14",
    "MACD", "MACD_signal", "MACD_hist",
    "BB_mid_20", "BB_upper_20", "BB_lower_20", "ATR_14",
    "STOCH_K", "STOCH_D", "ADX_14", "DI_plus", "DI_minus",
    "TENKAN_9", "KIJUN_26", "SENKOU_A", "SENKOU_B", "CHIKOU",
    "PSAR", "SUPERTREND", "MFI_14", "CCI_20",
    "DONCHIAN_upper_20", "DONCHIAN_mid_20", "DONCHIAN_lower_20",
    "OBV", "OBV_SMA_20",
    "ENGULFING", "HAMMER", "MARUBOZU",
];
Check(full.Columns.Select(c => c.Name).ToHashSet().SetEquals(expectedColumns),
    $"engine columns: {string.Join(',', full.Columns.Select(c => c.Name))}");
Check(full.Columns.Count == expectedColumns.Length,
    $"engine has {full.Columns.Count} columns, expected {expectedColumns.Length}");
Check(full.Columns.All(c => c.Values.Length == candles.Count), "engine column misaligned with candles");
Check(full.Stances.Count == 18, $"engine tracked {full.Stances.Count} stances, expected 18");
Check(full.Stances["ATR_14"].All(s => s == SignalDirection.Neutral), "ATR should never take a stance");
Check(full.Triggers.SequenceEqual(full.Triggers.OrderBy(t => t.Timestamp)), "triggers not chronological");

// Column dedup: two indicators sharing SMA_20 must not duplicate the column.
var dedup = new IndicatorEngine(
    [new SmaCrossoverIndicator(20, 50), new SmaCrossoverIndicator(20, 100)]).Run(candles);
Check(dedup.Columns.Select(c => c.Name).SequenceEqual(["SMA_20", "SMA_50", "SMA_100"]),
    $"dedup columns: {string.Join(',', dedup.Columns.Select(c => c.Name))}");

// --- OBV plugin indicator: math + stance ---
var obvResult = new ObvIndicator(20).Compute(candles);
Check(obvResult.Columns.Select(c => c.Name).SequenceEqual(["OBV", "OBV_SMA_20"]),
    $"OBV columns: {string.Join(',', obvResult.Columns.Select(c => c.Name))}");
var obvValues = obvResult.Columns[0].Values;
double expectedObvStep = candles[1].Close > candles[0].Close ? candles[1].Volume
    : candles[1].Close < candles[0].Close ? -candles[1].Volume : 0;
Check(obvValues[0] == 0 && obvValues[1] == expectedObvStep, "OBV accumulation wrong at start");

// --- PluginCatalog: discovers the OBV plugin DLL at runtime ---
var pluginResults = testCatalog.Indicators;
Check(pluginResults.Any(r => r.Indicator.Name == "OBV_20" && r.Source.Contains("Indicators.Advanced")),
    $"PluginCatalog missed OBV plugin; found: {string.Join(',', pluginResults.Select(r => r.Indicator.Name))}");

// --- PluginCatalog dedup: all indicators come from plugin DLLs, deduped by name ---
Check(fullSet.Count(r => r.Indicator.Name == "OBV_20") == 1, "registry duplicated or missed OBV_20");
Check(fullSet.Count(r => r.Indicator.Name == "RSI_14") == 1, "registry duplicated RSI_14");
Check(fullSet.Any(r => r.Source.Contains("Indicators.Standard")), "registry missed the Standard pack");
Check(fullSet.Any(r => r.Source.Contains("Indicators.Advanced")), "registry missed the Advanced pack");
Check(full.Stances.ContainsKey("OBV_20"), "engine from registry missing plugin stance");
Check(new PluginCatalog(Path.Combine(Path.GetTempPath(), "no-such-dir")).Indicators.Count == 0,
    "registry with no plugin dirs should be empty, not invent indicators");

// --- Trading agents: contract, scoring math, registry discovery ---
var trendCandles = MakeTrendingCandles(120, drift: +1.0);
var trendEngine = testCatalog.CreateEngine().Run(trendCandles);
var trendInput = new AgentInput("TEST", "1d", trendCandles, trendEngine);

var trendReport = new EricoreTech.CandleSharp.Agents.TrendFollowerAgent().Analyze(trendInput);
Check(trendReport.Signal.Direction == SignalDirection.Bullish,
    $"trend follower should be Bullish on a steady climb, got {trendReport.Signal.Direction}");
Check(trendReport.Signal.Confidence > 0 && trendReport.Signal.Confidence <= 100,
    $"trend confidence out of range: {trendReport.Signal.Confidence}");
Check(trendReport.Scores.Count == 3 && trendReport.Scores.All(s => s.MaxScore > 0),
    "trend follower scorecards missing contributors");

// Consensus math on a hand-built engine result: 2 bullish, 1 bearish, 1 neutral -> Bullish, 25%.
var handStances = new Dictionary<string, SignalDirection[]>
{
    ["A"] = [SignalDirection.Bullish],
    ["B"] = [SignalDirection.Bullish],
    ["C"] = [SignalDirection.Bearish],
    ["D"] = [SignalDirection.Neutral],
};
var handResult = new EngineResult([], [], handStances);
var handInput = new AgentInput("TEST", "1d", trendCandles.Take(1).ToList(), handResult);
var consensus = new EricoreTech.CandleSharp.Agents.ConsensusAgent().Analyze(handInput);
Check(consensus.Signal.Direction == SignalDirection.Bullish
    && Math.Abs(consensus.Signal.Confidence - 25) < 1e-9,
    $"consensus math wrong: {consensus.Signal.Direction} {consensus.Signal.Confidence}%");

// Agents degrade gracefully with nothing loaded.
var emptyInput = new AgentInput("TEST", "1d", trendCandles.Take(1).ToList(), new EngineResult([], [], new Dictionary<string, SignalDirection[]>()));
var emptyReport = new EricoreTech.CandleSharp.Agents.MeanReverterAgent().Analyze(emptyInput);
Check(emptyReport.Signal is { Direction: SignalDirection.Neutral, Confidence: 0 },
    "agent with no indicators should be Neutral at 0 confidence");

var agentSet = testCatalog.Agents;
Check(agentSet.Select(a => a.Agent.Key).OrderBy(k => k)
        .SequenceEqual(["consensus", "hedge", "mean_reverter", "ml_logistic", "regime", "risk_manager", "scorekeeper", "sentiment", "trend_follower"]),
    $"agent registry keys: {string.Join(',', agentSet.Select(a => a.Agent.Key))}");

// --- Backtester: walk-forward accounting on a known-outcome agent ---
var btEngine = testCatalog.CreateEngine();
var btReport = Backtester.Run(new AlwaysBullishAgent(), btEngine, "TEST", "1d", trendCandles,
    new BacktestOptions(Warmup: 20, Horizon: 5, Step: 5));
Check(btReport.Checkpoints == 19, $"expected 19 checkpoints (t=20..110 step 5), got {btReport.Checkpoints}");
Check(btReport.Directional == 19 && btReport.HitRate == 1.0,
    $"always-bullish on a steady climb should hit 100%: {btReport.Aligned}/{btReport.Directional}");
Check(btReport.CumulativeAlignedReturn > 0, "cumulative aligned return should be positive on a climb");

// --- ML Logistic: near-certain up-probability on a monotonic climb ---
var mlReport = new EricoreTech.CandleSharp.Agents.LogisticRegressionAgent().Analyze(trendInput);
Check(mlReport.Signal.Direction == SignalDirection.Bullish,
    $"logistic model should call a monotonic climb Bullish: {mlReport.Signal.Direction}");
Check(mlReport.Signal.Confidence is > 50 and <= 100,
    $"logistic confidence should be high on a sure thing: {mlReport.Signal.Confidence}");
Check(mlReport.Scores[0].Details.Any(d => d.StartsWith("Trained on")), "logistic scorecard missing training info");
var mlShort = new EricoreTech.CandleSharp.Agents.LogisticRegressionAgent().Analyze(
    new AgentInput("TEST", "1d", trendCandles.Take(40).ToList(),
        testCatalog.CreateEngine().Run(trendCandles.Take(40).ToList())));
Check(mlShort.Signal is { Direction: SignalDirection.Neutral, Confidence: 0 },
    "logistic agent should refuse to predict without enough samples");

// --- Hedge meta-agent: learns to trust the trend follower on a trend ---
var hedgeReport = new EricoreTech.CandleSharp.Agents.HedgeMetaAgent().Analyze(trendInput);
Check(hedgeReport.Signal.Direction == SignalDirection.Bullish,
    $"hedge should be Bullish on a steady climb: {hedgeReport.Signal.Direction}");
var trust = hedgeReport.Scores[0].Details;
double TrustOf(string key) => double.Parse(
    trust.First(d => d.StartsWith(key + ":")).Split("weight ")[1].Split(',')[0],
    System.Globalization.CultureInfo.InvariantCulture);
Check(TrustOf("trend_follower") >= TrustOf("mean_reverter"),
    $"hedge should trust the trend follower at least as much as the mean reverter on a trend " +
    $"(trend {TrustOf("trend_follower")}, mean-revert {TrustOf("mean_reverter")})");
Check(Math.Abs(TrustOf("trend_follower") + TrustOf("mean_reverter") + TrustOf("consensus") + TrustOf("scorekeeper") - 1) < 0.01,
    "hedge trust weights should be normalized");

// Scorekeeper: hand-built history where one indicator's triggers always land
// and another's always miss. On a monotonic climb every Bullish trigger hits
// and every Bearish trigger misses.
var skStances = new Dictionary<string, SignalDirection[]>
{
    ["GOOD"] = Enumerable.Repeat(SignalDirection.Neutral, 119).Append(SignalDirection.Bullish).ToArray(),
    ["BAD"] = Enumerable.Repeat(SignalDirection.Neutral, 119).Append(SignalDirection.Bearish).ToArray(),
};
var skTriggers = new List<Trigger>();
foreach (int i in new[] { 5, 15, 25 })
{
    skTriggers.Add(new Trigger(trendCandles[i].Timestamp, "GOOD", SignalDirection.Bullish));
    skTriggers.Add(new Trigger(trendCandles[i].Timestamp, "BAD", SignalDirection.Bearish));
}
var skInput = new AgentInput("TEST", "1d", trendCandles, new EngineResult([], skTriggers, skStances));
var skReport = new EricoreTech.CandleSharp.Agents.ScorekeeperAgent(horizon: 10, minTriggers: 3).Analyze(skInput);
Check(skReport.Signal.Direction == SignalDirection.Bullish && skReport.Signal.Confidence == 100,
    $"scorekeeper should fully back the 100%-hit-rate indicator: {skReport.Signal.Direction} {skReport.Signal.Confidence}%");
Check(skReport.Scores[0].Details.Any(d => d.StartsWith("GOOD: 3/3")), "scorekeeper GOOD track record wrong");
Check(skReport.Scores[0].Details.Any(d => d.StartsWith("BAD: 0/3")), "scorekeeper BAD track record wrong");

// Regime: a steady climb reads as trending and defers to the ADX direction.
var regimeReport = new EricoreTech.CandleSharp.Agents.RegimeAgent().Analyze(trendInput);
Check(regimeReport.Signal.Reasoning.Contains("trending"),
    $"regime should read a steady climb as trending: {regimeReport.Signal.Reasoning}");
Check(regimeReport.Signal.Direction == SignalDirection.Bullish, "regime direction should follow ADX in a trend");

// Risk manager: non-directional, sizes within the account cap.
var riskReport = new EricoreTech.CandleSharp.Agents.RiskManagerAgent(accountValue: 10_000, riskPercent: 1).Analyze(trendInput);
Check(riskReport.Signal.Direction == SignalDirection.Neutral && riskReport.Signal.Confidence == 0,
    "risk manager must be non-directional");
Check(riskReport.Signal.Reasoning.Contains("shares"), "risk manager reasoning missing size");
double lastClose = trendCandles[^1].Close;
int suggested = (int)riskReport.Scores[0].Score;
Check(suggested > 0 && suggested * lastClose <= 10_000 * 0.20 + lastClose,
    $"risk manager size {suggested} breaches the 20% cap");

var (runDir, runSince) = Stances.LatestRun(
    [SignalDirection.Bearish, SignalDirection.Bullish, SignalDirection.Bullish]);
Check(runDir == SignalDirection.Bullish && runSince == 1, "LatestRun wrong");

// --- VerdictComposer: confidence-weighted vote with a lean threshold ---
static AgentReport Report(string key, SignalDirection dir, double conf) =>
    new(key, key, "TEST", new TradeSignal(dir, conf, $"{key} reasoning"), []);

var verdictInput = new AgentInput("TEST", "1d", trendCandles,
    new EngineResult([], [], new Dictionary<string, SignalDirection[]>()));

// bull 140 vs bear 40 -> net +0.56 -> BUY at 56%.
var buyVerdict = VerdictComposer.Compose(verdictInput,
[
    Report("a", SignalDirection.Bullish, 80),
    Report("b", SignalDirection.Bullish, 60),
    Report("c", SignalDirection.Bearish, 40),
]);
Check(buyVerdict.Direction == SignalDirection.Bullish && buyVerdict.Confidence == 56,
    $"verdict weighting wrong: {buyVerdict.Direction} {buyVerdict.Confidence}%");
Check(buyVerdict.Headline.Contains("LEANING BUY") && buyVerdict.Headline.Contains("TEST"),
    $"verdict headline wrong: {buyVerdict.Headline}");
Check(buyVerdict.Details.Any(d => d.Contains("1 bearish")), "verdict agent tally missing");
Check(buyVerdict.Details[^1].Contains("not financial advice"), "verdict must carry the disclaimer");

// Evenly split -> inside the lean threshold -> no clear edge.
var splitVerdict = VerdictComposer.Compose(verdictInput,
[
    Report("a", SignalDirection.Bullish, 50),
    Report("b", SignalDirection.Bearish, 50),
]);
Check(splitVerdict.Direction == SignalDirection.Neutral && splitVerdict.Headline.Contains("NO CLEAR EDGE"),
    $"split vote should read as no edge: {splitVerdict.Headline}");

// All-neutral agents -> no edge, and the risk manager never votes.
var neutralVerdict = VerdictComposer.Compose(verdictInput,
[
    Report("a", SignalDirection.Neutral, 0),
    Report("risk_manager", SignalDirection.Bullish, 100),
]);
Check(neutralVerdict.Direction == SignalDirection.Neutral,
    "risk_manager must not swing the verdict");
Check(neutralVerdict.Details.Any(d => d.StartsWith("If you do trade it")),
    "risk manager guidance should still appear in the details");

// --- DividendAnalytics: yield and growth from a clean 5%-per-year grower ---
// Quarterly payments 2019..mid-2026, each year's quarterly amount 5% above the
// prior year's. As-of date comes from the last candle (2026-06-30).
var divPayments = new List<Dividend>();
for (int year = 2019; year <= 2026; year++)
{
    double quarterly = 0.50 * Math.Pow(1.05, year - 2019);
    foreach (int month in new[] { 3, 6, 9, 12 })
    {
        if (year == 2026 && month > 6) continue;
        divPayments.Add(new Dividend(new DateTime(year, month, 15), quarterly));
    }
}
var divCandles = new List<Candle> { new(new DateTime(2026, 6, 30), 100, 100, 100, 100, 1000) };
var divSummary = DividendAnalytics.Summarize("DIV", divPayments, divCandles);

Check(divSummary.PaymentCount == divPayments.Count, "dividend payment count wrong");
// Trailing year from 2026-06-30 back: 2026 Mar+Jun + 2025 Sep+Dec.
double expectedTtm = 2 * 0.50 * Math.Pow(1.05, 7) + 2 * 0.50 * Math.Pow(1.05, 6);
Check(Math.Abs(divSummary.TrailingYearTotal - expectedTtm) < 1e-9, "trailing-year dividend sum wrong");
Check(Math.Abs(divSummary.YieldPercent!.Value - expectedTtm) < 1e-9, "yield wrong (close=100 so yield% == ttm)");
Check(Math.Abs(divSummary.GrowthYoYPercent!.Value - 5) < 1e-6, $"YoY growth should be 5%, got {divSummary.GrowthYoYPercent}");
Check(Math.Abs(divSummary.Growth3YPercent!.Value - 5) < 1e-6, $"3y CAGR should be 5%, got {divSummary.Growth3YPercent}");
Check(Math.Abs(divSummary.Growth5YPercent!.Value - 5) < 1e-6, $"5y CAGR should be 5%, got {divSummary.Growth5YPercent}");
Check(divSummary.ConsecutiveGrowthYears == 6, $"expected 6 consecutive growth years (2020-2025), got {divSummary.ConsecutiveGrowthYears}");
Check(DividendAnalytics.Summarize("NONE", [], divCandles).PaymentCount == 0, "empty dividends should summarize gracefully");

// --- Dividend storage: round-trip, merge, and exclusion from the dataset list ---
var divDir = Path.Combine(Path.GetTempPath(), $"candlesharp-div-tests-{Guid.NewGuid():N}");
try
{
    var divRepo = new CsvCandleRepository(divDir);
    divRepo.SaveDividends(divPayments.Take(10).ToList(), "DIV");
    divRepo.SaveDividends(divPayments.Skip(8).ToList(), "DIV");
    var loadedDivs = divRepo.LoadDividends("DIV");
    Check(loadedDivs.Count == divPayments.Count, $"dividend merge produced {loadedDivs.Count}, expected {divPayments.Count}");
    Check(Math.Abs(loadedDivs[^1].Amount - divPayments[^1].Amount) < 1e-6, "dividend amount mangled by round-trip");

    divRepo.Save(divCandles, "DIV", "1d");
    Check(divRepo.List().Count == 1 && divRepo.List()[0].Interval == "1d",
        "dividend files must not appear as datasets");
}
finally
{
    if (Directory.Exists(divDir)) Directory.Delete(divDir, recursive: true);
}

// --- RefreshAll: fetchable datasets top up via the feed, derived ones re-resample ---
var refreshDir = Path.Combine(Path.GetTempPath(), $"candlesharp-refresh-tests-{Guid.NewGuid():N}");
try
{
    var refreshRepo = new CsvCandleRepository(refreshDir);
    refreshRepo.Save(trendCandles.Take(100).ToList(), "AAA", "1d");
    refreshRepo.Save(Resampler.Aggregate(trendCandles.Take(100).ToList(), AggregatePeriod.Weekly), "AAA", "1wk");
    refreshRepo.Save(trendCandles.Take(50).ToList(), "BBB", "1d");

    var fakeFeed = new FakeQuoteFeed();
    fakeFeed.Responses["AAA"] = new QuoteHistory(
        trendCandles.Skip(90).ToList(),
        [new Dividend(trendCandles[110].Timestamp, 0.25)]);
    // BBB has no scripted data -> the feed throws -> must land in warnings.

    var fakeSocialFeed = new FakeSocialFeed();
    fakeSocialFeed.Responses["AAA"] =
        [new SocialPost(trendCandles[110].Timestamp, "poster", "buy the breakout", SignalDirection.Bullish)];
    // BBB social also unscripted -> a second warning, never a stop.
    var refreshSocialStore = new JsonSocialStore(refreshDir);

    var refreshMarket = new MarketDataService(
        refreshRepo, refreshRepo, refreshSocialStore, fakeSocialFeed, fakeFeed);
    var refreshOutcome = refreshMarket.RefreshAllAsync().GetAwaiter().GetResult();

    Check(refreshOutcome.Refreshed.Count == 2, $"expected 2 refreshed datasets, got {refreshOutcome.Refreshed.Count}");
    Check(refreshOutcome.Refreshed.Any(r => r is { Ticker: "AAA", Interval: "1d", Mode: "fetched", Dividends: 1 }),
        "AAA daily should be fetched with its dividend");
    Check(refreshOutcome.Refreshed.Any(r => r is { Ticker: "AAA", Interval: "1wk", Mode: "derived" }),
        "AAA weekly should be re-derived locally, not fetched");
    Check(fakeFeed.Requests.All(r => r.Interval == "1d"), "no feed request should ask for a derived interval");
    Check(refreshOutcome.Warnings.Count == 2 && refreshOutcome.Warnings.All(w => w.StartsWith("BBB")),
        $"BBB's feed and social failures should both be warnings: {string.Join(';', refreshOutcome.Warnings)}");
    Check(refreshOutcome.SocialPulls is [{ Ticker: "AAA", Posts: 1 }],
        $"refresh should pull AAA chatter once: {refreshOutcome.SocialPulls.Count} pull(s)");
    Check(refreshSocialStore.LoadPosts("AAA").Count == 1, "pulled chatter should be saved locally");

    Check(refreshRepo.Load("AAA", "1d").Count == 120, "AAA daily should have merged up to 120 bars");
    Check(refreshRepo.LoadDividends("AAA").Count == 1, "AAA dividend should be stored");
    Check(refreshRepo.Load("BBB", "1d").Count == 50, "BBB data must be untouched after a failed refresh");
}
finally
{
    if (Directory.Exists(refreshDir)) Directory.Delete(refreshDir, recursive: true);
}

// --- Simulator: raw-price reconstruction round-trip and buy-and-hold accounting ---
// Known raw world: flat 100 close for 10 bars, $1 dividend ex bar 5. Yahoo-style
// adjustment multiplies bars 0..4 by (100-1)/100 = 0.99. Feed the ADJUSTED
// series in and the simulator must recover the raw prices and pay cash right.
var simBase = new DateTime(2025, 1, 6);
var simCandles = Enumerable.Range(0, 10)
    .Select(i =>
    {
        double adj = i < 5 ? 99.0 : 100.0;
        return new Candle(simBase.AddDays(i), adj, adj, adj, adj, 1000);
    })
    .ToList();
var simDividends = new List<Dividend> { new(simBase.AddDays(5), 1.0) };

var rawCloses = Simulator.ReconstructRawCloses(simCandles, simDividends);
Check(rawCloses.All(v => Math.Abs(v - 100.0) < 1e-9),
    $"raw reconstruction should recover flat 100, got first={rawCloses[0]:0.####}");

var sim = Simulator.BuyAndHold("SIM", simCandles, simDividends, 1000, simBase, simBase.AddDays(9));
Check(Math.Abs(sim.Shares - 10) < 1e-9, $"should buy 10 shares at raw 100, got {sim.Shares}");
Check(sim.DividendPayments == 1 && Math.Abs(sim.DividendCash - 10) < 1e-9,
    $"10 shares x $1 dividend should pay $10, got {sim.DividendCash}");
Check(Math.Abs(sim.EndStockValue - 1000) < 1e-9, "flat raw price should keep stock value at 1000");
Check(Math.Abs(sim.EndTotalValue - 1010) < 1e-9 && Math.Abs(sim.TotalReturnPercent - 1.0) < 1e-9,
    $"total should be 1010 (+1%), got {sim.EndTotalValue} ({sim.TotalReturnPercent}%)");
double expectedReinvested = 1000 * 100.0 / 99.0;
Check(Math.Abs(sim.ReinvestedValue - expectedReinvested) < 1e-9,
    $"reinvested value should be {expectedReinvested:0.00}, got {sim.ReinvestedValue:0.00}");

// Buying after the ex-date must not collect the dividend.
var simLate = Simulator.BuyAndHold("SIM", simCandles, simDividends, 1000, simBase.AddDays(5), simBase.AddDays(9));
Check(simLate.DividendPayments == 0 && simLate.DividendCash == 0,
    "buying on the ex-date should collect nothing");

Check(Throws(() => Simulator.BuyAndHold("SIM", simCandles, simDividends, 0, simBase, simBase.AddDays(9))),
    "zero investment must throw");
bool badRange = false;
try { Simulator.BuyAndHold("SIM", simCandles, simDividends, 1000, simBase.AddDays(20), simBase.AddDays(30)); }
catch (ArgumentException) { badRange = true; }
Check(badRange, "out-of-range dates must throw");

// --- Social sentiment: lexicon, summary math, and the conservative agent ---
Check(SocialAnalytics.Score("to the moon, buy calls, breakout!") > 0.5, "clearly bullish text should score positive");
Check(SocialAnalytics.Score("dump this bagholder trap, buying puts") < -0.5, "clearly bearish text should score negative");
Check(SocialAnalytics.Score("earnings call is on tuesday") == 0, "neutral text should score zero");

var socialBase = new DateTime(2026, 6, 10);
var socialPosts = new List<SocialPost>();
for (int i = 0; i < 8; i++)
    socialPosts.Add(new SocialPost(socialBase.AddHours(i), $"bull{i}", "going up", SignalDirection.Bullish));
for (int i = 0; i < 2; i++)
    socialPosts.Add(new SocialPost(socialBase.AddHours(10 + i), $"bear{i}", "going down", SignalDirection.Bearish));
socialPosts.Add(new SocialPost(socialBase.AddHours(13), "lex1", "huge breakout, buy the dip", SignalDirection.Neutral));
socialPosts.Add(new SocialPost(socialBase.AddHours(14), "lex2", "what time is lunch", SignalDirection.Neutral));

var socialSummary = SocialAnalytics.Summarize("SOC", socialPosts);
Check(socialSummary is { PostCount: 12, TaggedBullish: 8, TaggedBearish: 2, LexiconBullish: 1, LexiconBearish: 0 },
    $"social tally wrong: {socialSummary.TaggedBullish}/{socialSummary.TaggedBearish}/{socialSummary.LexiconBullish}/{socialSummary.LexiconBearish}");
Check(Math.Abs(socialSummary.BullishRatioPercent!.Value - 9.0 / 11 * 100) < 1e-9,
    $"bullish ratio should be 9/11, got {socialSummary.BullishRatioPercent}");

var sentimentCandles = new List<Candle> { new(socialBase.AddDays(1), 100, 100, 100, 100, 1000) };
var sentimentInput = new AgentInput("SOC", "1d", sentimentCandles,
    new EngineResult([], [], new Dictionary<string, SignalDirection[]>()), socialSummary);
var sentimentReport = new EricoreTech.CandleSharp.Agents.SentimentAgent().Analyze(sentimentInput);
Check(sentimentReport.Signal.Direction == SignalDirection.Bullish && sentimentReport.Signal.Confidence > 0,
    $"9/11 bullish crowd should read Bullish: {sentimentReport.Signal.Direction} ({sentimentReport.Signal.Reasoning})");

var noSocial = new EricoreTech.CandleSharp.Agents.SentimentAgent().Analyze(sentimentInput with { Social = null });
Check(noSocial.Signal is { Direction: SignalDirection.Neutral, Confidence: 0 }, "no social data must be Neutral");

var thinCrowd = SocialAnalytics.Summarize("SOC", socialPosts.Take(4).ToList());
var thinReport = new EricoreTech.CandleSharp.Agents.SentimentAgent().Analyze(sentimentInput with { Social = thinCrowd });
Check(thinReport.Signal.Direction == SignalDirection.Neutral, "a thin crowd must not move the agent");

var staleCandles = new List<Candle> { new(socialBase.AddDays(30), 100, 100, 100, 100, 1000) };
var staleReport = new EricoreTech.CandleSharp.Agents.SentimentAgent().Analyze(
    new AgentInput("SOC", "1d", staleCandles, new EngineResult([], [], new Dictionary<string, SignalDirection[]>()), socialSummary));
Check(staleReport.Signal.Direction == SignalDirection.Neutral && staleReport.Signal.Reasoning.Contains("stale"),
    "month-old chatter must read as stale");

var socialDir = Path.Combine(Path.GetTempPath(), $"candlesharp-social-tests-{Guid.NewGuid():N}");
try
{
    var socialStore = new JsonSocialStore(socialDir);
    socialStore.SavePosts(socialPosts.Take(8).ToList(), "SOC");
    socialStore.SavePosts(socialPosts.Skip(6).ToList(), "SOC");
    Check(socialStore.LoadPosts("SOC").Count == socialPosts.Count,
        $"social merge should dedupe to {socialPosts.Count}");
}
finally
{
    if (Directory.Exists(socialDir)) Directory.Delete(socialDir, recursive: true);
}

if (failures == 0)
{
    Console.WriteLine("All tests passed.");
    return 0;
}
Console.Error.WriteLine($"{failures} test(s) FAILED.");
return 1;

void Check(bool condition, string message)
{
    if (condition) return;
    failures++;
    Console.Error.WriteLine($"FAIL: {message}");
}

static bool Throws(Action action)
{
    try { action(); return false; }
    catch (ArgumentOutOfRangeException) { return true; }
}

static List<Candle> MakeTrendingCandles(int count, double drift)
{
    var candles = new List<Candle>(count);
    double close = 100;
    var date = new DateTime(2024, 1, 1);
    for (int i = 0; i < count; i++)
    {
        double open = close;
        close += drift;
        candles.Add(new Candle(date, open, Math.Max(open, close) + 0.5,
            Math.Min(open, close) - 0.5, close, 2_000_000));
        date = date.AddDays(1);
    }
    return candles;
}

static List<Candle> MakeSyntheticCandles(int count)
{
    // Deterministic pseudo-random walk so failures are reproducible.
    var rng = new Random(42);
    var candles = new List<Candle>(count);
    double close = 100;
    var date = new DateTime(2024, 1, 1);
    for (int i = 0; i < count; i++)
    {
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            date = date.AddDays(1);
        double open = close + (rng.NextDouble() - 0.5);
        close = Math.Max(1, close + (rng.NextDouble() - 0.48) * 3);
        double high = Math.Max(open, close) + rng.NextDouble();
        double low = Math.Min(open, close) - rng.NextDouble();
        candles.Add(new Candle(date, open, high, low, close, rng.Next(1_000_000, 5_000_000)));
        date = date.AddDays(1);
    }
    return candles;
}

