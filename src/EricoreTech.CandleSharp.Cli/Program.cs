using System.Globalization;
using EricoreTech.CandleSharp.Application;
using EricoreTech.CandleSharp.Domain;
using EricoreTech.CandleSharp.Infrastructure;

return args.Length == 0 ? Usage() : await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    var opts = ParseOptions(args, out var positional);
    if (positional.Count == 0) return Usage();

    // Composition root: infrastructure adapters wired into application services.
    var csvRepository = new CsvCandleRepository(opts.GetValueOrDefault("data-dir", "data"));
    ICandleRepository repository = csvRepository;
    IDividendRepository dividendRepository = csvRepository;
    IPluginCatalog catalog = new PluginCatalog(
        opts.GetValueOrDefault("plugins", "plugins"),
        error => Console.Error.WriteLine($"plugin warning: {error}"));
    ISocialRepository socialRepository = new JsonSocialStore(repository.DataDirectory);
    using var feed = new YahooFinanceClient();
    using var socialFeed = new StockTwitsClient();
    var market = new MarketDataService(repository, dividendRepository, socialRepository, socialFeed, feed);
    var analysis = new AnalysisService(repository, catalog, socialRepository);
    var simulation = new SimulationService(repository, dividendRepository);
    var social = new SocialService(socialRepository, socialFeed);
    var reports = new ReportService(analysis, market, simulation, social);
    var patterns = new PatternService(repository, new CsvSnapshotRepository(repository.DataDirectory), catalog);
    var ruleService = new RuleService(repository, new JsonRuleStore(repository.DataDirectory), catalog);
    var mlGuidance = new MLGuidanceService(repository, catalog);
    var watch = new WatchService(repository, dividendRepository, socialRepository, catalog, new JsonAgentStateStore(repository.DataDirectory), feed);

    var command = positional[0];
    var rest = positional.Skip(1).ToList();

    try
    {
        return command switch
        {
            "fetch" => await FetchAsync(rest, opts, market),
            "refresh" => await RefreshAsync(opts, market),
            "list" => ListDatasets(market, repository),
            "indicators" => RunIndicators(rest, opts, analysis, repository, catalog),
            "signals" => RunSignals(rest, opts, analysis, catalog),
            "resample" => Resample(rest, opts, market),
            "dividends" => RunDividends(rest, market),
            "simulate" => RunSimulate(rest, opts, simulation),
            "social" => await RunSocialAsync(rest, opts, social),
            "analyze" => RunAnalyze(rest, opts, analysis, catalog),
            "verdict" => RunVerdict(rest, opts, analysis, catalog),
            "screen" => RunScreen(opts, analysis, catalog),
            "backtest" => RunBacktest(rest, opts, analysis, catalog),
            "watch" => await RunWatchAsync(rest, opts, watch, catalog),
            "report" => RunReport(rest, opts, reports, market),
            "snapshot" => RunSnapshot(rest, opts, patterns, catalog),
            "patterns" => RunPatterns(rest, opts, patterns, catalog),
            "rules" => RunRules(rest, opts, ruleService),
            "ml" => RunMLGuidance(rest, opts, mlGuidance),
            "rule" => RunRule(rest, opts, ruleService),
            _ => Usage($"Unknown command: {command}"),
        };
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static async Task<int> FetchAsync(List<string> tickers, Dictionary<string, string> opts, MarketDataService market)
{
    if (tickers.Count == 0) return Usage("fetch needs at least one ticker, e.g.: fetch AAPL MSFT");

    var period = opts.GetValueOrDefault("period", "1y");
    var interval = opts.GetValueOrDefault("interval", "1d");
    DateOnly? start = opts.TryGetValue("start", out var s) ? ParseDate(s) : null;
    DateOnly? end = opts.TryGetValue("end", out var e) ? ParseDate(e) : null;

    bool anyFailed = false;
    foreach (var ticker in tickers)
    {
        try
        {
            var result = await market.FetchAsync(ticker, period, interval, start, end);
            var dividendNote = result.Dividends > 0 ? $" (+{result.Dividends} dividend payments)" : "";
            Console.WriteLine($"{result.Ticker}: {result.Bars} bars -> {result.Path}{dividendNote}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{ticker}: FAILED ({ex.Message})");
            anyFailed = true;
        }
    }
    return anyFailed ? 1 : 0;
}

static async Task<int> RefreshAsync(Dictionary<string, string> opts, MarketDataService market)
{
    var result = await market.RefreshAllAsync(opts.GetValueOrDefault("period", "3mo"));
    foreach (var dataset in result.Refreshed)
    {
        var dividendNote = dataset.Dividends > 0 ? $" (+{dataset.Dividends} dividend payments)" : "";
        Console.WriteLine($"{dataset.Ticker,-8} {dataset.Interval,-4} {dataset.Bars,6} bars ({dataset.Mode}){dividendNote}");
    }
    foreach (var pull in result.SocialPulls)
        Console.WriteLine($"{pull.Ticker,-8} chatter: {pull.Posts} posts pulled");
    foreach (var warning in result.Warnings)
        Console.Error.WriteLine($"warning: {warning}");
    Console.WriteLine(
        $"Refreshed {result.Refreshed.Count} dataset(s), pulled chatter for {result.SocialPulls.Count} ticker(s), {result.Warnings.Count} failure(s).");
    return result.Refreshed.Count == 0 && result.Warnings.Count > 0 ? 1 : 0;
}

static int ListDatasets(MarketDataService market, ICandleRepository repository)
{
    var datasets = market.ListDatasets();
    if (datasets.Count == 0)
    {
        Console.WriteLine("No local data yet. Run: candlesharp fetch AAPL");
        return 0;
    }
    foreach (var dataset in datasets)
    {
        var candles = repository.Load(dataset.Ticker, dataset.Interval);
        Console.WriteLine(
            $"{dataset.Ticker,-8} {dataset.Interval,-4} {candles.Count,6} bars  " +
            $"{candles[0].Timestamp:yyyy-MM-dd} .. {candles[^1].Timestamp:yyyy-MM-dd}  ({dataset.Path})");
    }
    return 0;
}

static int RunIndicators(List<string> rest, Dictionary<string, string> opts,
    AnalysisService analysis, ICandleRepository repository, IPluginCatalog catalog)
{
    if (rest.Count == 0) return Usage("indicators needs a ticker, e.g.: indicators AAPL");

    ReportPlugins(catalog);
    var series = analysis.GetSeries(rest[0], opts.GetValueOrDefault("interval", "1d"));
    int tail = opts.TryGetValue("tail", out var t) ? int.Parse(t, CultureInfo.InvariantCulture) : 10;

    var outPath = Path.Combine(repository.DataDirectory,
        $"{series.Ticker}_{series.Interval}_indicators.csv");
    WriteIndicatorsCsv(outPath, series.Candles, series.Signals.Columns);
    Console.WriteLine($"Wrote {series.Candles.Count} rows with indicators -> {outPath}");

    PrintTail(series.Candles, series.Signals.Columns, tail);
    return 0;
}

static int RunSignals(List<string> rest, Dictionary<string, string> opts,
    AnalysisService analysis, IPluginCatalog catalog)
{
    if (rest.Count == 0) return Usage("signals needs a ticker, e.g.: signals AAPL");

    ReportPlugins(catalog);
    var series = analysis.GetSeries(rest[0], opts.GetValueOrDefault("interval", "1d"));
    int tail = opts.TryGetValue("tail", out var t) ? int.Parse(t, CultureInfo.InvariantCulture) : 10;

    Console.WriteLine($"Current stance ({series.Ticker}, {series.Interval}, as of {series.Candles[^1].Timestamp:yyyy-MM-dd}):");
    foreach (var (name, stance) in series.Signals.Stances.OrderBy(s => s.Key))
    {
        var (current, since) = Stances.LatestRun(stance);
        Console.WriteLine($"  {name,-18} {current,-8} (since {series.Candles[since].Timestamp:yyyy-MM-dd})");
    }

    Console.WriteLine();
    if (series.Signals.Triggers.Count == 0)
    {
        Console.WriteLine("No triggers in the loaded history.");
        return 0;
    }
    Console.WriteLine($"Last {Math.Min(tail, series.Signals.Triggers.Count)} of {series.Signals.Triggers.Count} triggers:");
    foreach (var trigger in series.Signals.Triggers.TakeLast(tail))
        Console.WriteLine($"  {trigger.Timestamp:yyyy-MM-dd}  {trigger.Indicator,-18} {trigger.Direction}");
    return 0;
}

static int Resample(List<string> rest, Dictionary<string, string> opts, MarketDataService market)
{
    if (rest.Count == 0) return Usage("resample needs a ticker, e.g.: resample AAPL --to 1wk");

    var result = market.Resample(
        rest[0],
        opts.GetValueOrDefault("interval", "1d"),
        opts.GetValueOrDefault("to", "1wk"));
    Console.WriteLine(
        $"{result.Ticker}: {result.SourceBars} {result.SourceInterval} bars -> " +
        $"{result.TargetBars} {result.TargetInterval} bars -> {result.Path}");
    return 0;
}

static int RunDividends(List<string> rest, MarketDataService market)
{
    if (rest.Count == 0) return Usage("dividends needs a ticker, e.g.: dividends KO");

    var summary = market.GetDividendSummary(rest[0]);
    if (summary.PaymentCount == 0)
    {
        Console.WriteLine($"No dividend data stored for {summary.Ticker}. Fetch the ticker first (dividends arrive with price data).");
        return 0;
    }

    Console.WriteLine($"Dividends for {summary.Ticker}:");
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"  Payments on record: {summary.PaymentCount} (last {summary.LastAmount:0.####} on {summary.LastPaymentDate:yyyy-MM-dd})"));
    var trailing = string.Create(CultureInfo.InvariantCulture,
        $"  Trailing 12 months: {summary.TrailingYearTotal:0.####} per share");
    if (summary.YieldPercent is { } y)
        trailing += string.Create(CultureInfo.InvariantCulture, $" -> current yield {y:0.0}%");
    Console.WriteLine(trailing);
    Console.WriteLine("  Growth: "
        + (summary.GrowthYoYPercent is { } g1 ? string.Create(CultureInfo.InvariantCulture, $"{g1:+0.0;-0.0}% YoY") : "YoY n/a")
        + (summary.Growth3YPercent is { } g3 ? string.Create(CultureInfo.InvariantCulture, $", {g3:+0.0;-0.0}%/yr over 3y") : "")
        + (summary.Growth5YPercent is { } g5 ? string.Create(CultureInfo.InvariantCulture, $", {g5:+0.0;-0.0}%/yr over 5y") : "")
        + $"; {summary.ConsecutiveGrowthYears} consecutive growth year(s)");
    Console.WriteLine("  Annual totals: " + string.Join("  ", summary.AnnualTotals.Select(a =>
        string.Create(CultureInfo.InvariantCulture, $"{a.Year} {a.Total:0.####}"))));
    Console.WriteLine("  Recent payments:");
    foreach (var payment in summary.RecentPayments)
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"    {payment.Timestamp:yyyy-MM-dd}  {payment.Amount:0.####}"));
    return 0;
}

static int RunSimulate(List<string> rest, Dictionary<string, string> opts, SimulationService simulation)
{
    if (rest.Count == 0) return Usage("simulate needs a ticker, e.g.: simulate KO --amount 10000");

    double amount = double.Parse(opts.GetValueOrDefault("amount", "10000"), CultureInfo.InvariantCulture);
    DateTime? start = opts.TryGetValue("start", out var s) ? ParseDate(s).ToDateTime(TimeOnly.MinValue) : null;
    DateTime? end = opts.TryGetValue("end", out var e) ? ParseDate(e).ToDateTime(TimeOnly.MinValue) : null;

    var r = simulation.BuyAndHold(rest[0], amount, start, end);
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"Buy & hold simulation: {r.Ticker}"));
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"  Invested {r.Invested:0.00} on {r.BuyDate:yyyy-MM-dd} at {r.BuyPrice:0.00} -> {r.Shares:0.####} shares"));
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"  Held until {r.SellDate:yyyy-MM-dd} at {r.SellPrice:0.00}"));
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"  Stock value {r.EndStockValue:0.00} + dividends collected {r.DividendCash:0.00} ({r.DividendPayments} payments)"));
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"  TOTAL: {r.EndTotalValue:0.00} ({r.TotalReturnPercent:+0.0;-0.0}%) = price {r.PriceReturnPercent:+0.0;-0.0}% + dividends {r.DividendYieldOnCostPercent:+0.0;-0.0}% on cost"));
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"  If dividends reinvested: {r.ReinvestedValue:0.00} ({r.ReinvestedReturnPercent:+0.0;-0.0}%)"));
    return 0;
}

static async Task<int> RunSocialAsync(List<string> rest, Dictionary<string, string> opts, SocialService social)
{
    if (rest.Count == 0) return Usage("social needs a ticker, e.g.: social KO --fetch");

    var ticker = rest[0];
    if (opts.GetValueOrDefault("fetch", "false") == "true")
    {
        try
        {
            int added = await social.FetchAndStoreAsync(ticker);
            Console.WriteLine($"Pulled {added} posts from StockTwits.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"warning: social fetch failed, showing stored posts ({ex.Message})");
        }
    }

    var summary = social.GetSummary(ticker);
    if (summary.PostCount == 0)
    {
        Console.WriteLine($"No social posts stored for {summary.Ticker}. Run: candlesharp social {summary.Ticker} --fetch");
        return 0;
    }

    Console.WriteLine($"Crowd sentiment for {summary.Ticker}:");
    Console.WriteLine($"  {summary.PostCount} posts from {summary.OldestPost:yyyy-MM-dd} to {summary.NewestPost:yyyy-MM-dd}");
    Console.WriteLine($"  Author-tagged: {summary.TaggedBullish} bullish vs {summary.TaggedBearish} bearish; lexicon-read: {summary.LexiconBullish} vs {summary.LexiconBearish}");
    if (summary.BullishRatioPercent is { } ratio)
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  Bullish ratio {ratio:0}%; average lexicon score {summary.AverageLexiconScore:+0.00;-0.00}"));
    Console.WriteLine("  Recent posts:");
    foreach (var post in summary.RecentPosts)
    {
        var text = post.Text.Length > 90 ? post.Text[..90] + "..." : post.Text;
        var tag = post.Tagged == SignalDirection.Neutral ? "" : $" [{post.Tagged}]";
        Console.WriteLine($"    {post.Timestamp:MM-dd} @{post.Author}{tag}: {text.ReplaceLineEndings(" ")}");
    }
    return 0;
}

static int RunAnalyze(List<string> rest, Dictionary<string, string> opts,
    AnalysisService analysis, IPluginCatalog catalog)
{
    if (rest.Count == 0) return Usage("analyze needs a ticker, e.g.: analyze AAPL");

    ReportPlugins(catalog);
    var interval = opts.GetValueOrDefault("interval", "1d");
    var keys = opts.TryGetValue("agents", out var filter)
        ? filter.Split(',', StringSplitOptions.TrimEntries)
        : null;
    var reports = analysis.Analyze(rest[0], interval, keys);
    if (reports.Count == 0)
    {
        Console.Error.WriteLine("warning: no trading agents matched — build the solution first or check --agents");
        return 1;
    }

    foreach (var report in reports)
    {
        Console.WriteLine();
        Console.WriteLine($"{report.DisplayName} [{report.Key}] on {report.Ticker} ({interval}):");
        Console.WriteLine($"  {Icon(report.Signal.Direction)} {report.Signal.Direction}  confidence {report.Signal.Confidence:0}%");
        Console.WriteLine($"  {report.Signal.Reasoning}");
        foreach (var card in report.Scores)
        {
            Console.WriteLine($"  {card.Title} [{card.Score:+0;-0;0}/{card.MaxScore}]");
            foreach (var detail in card.Details)
                Console.WriteLine($"    - {detail}");
        }
    }
    return 0;
}

static int RunVerdict(List<string> rest, Dictionary<string, string> opts,
    AnalysisService analysis, IPluginCatalog catalog)
{
    if (rest.Count == 0) return Usage("verdict needs a ticker, e.g.: verdict AAPL");

    ReportPlugins(catalog);
    var verdict = analysis.GetVerdict(rest[0], opts.GetValueOrDefault("interval", "1d"));
    Console.WriteLine();
    Console.WriteLine($"{Icon(verdict.Direction)} {verdict.Headline}");
    Console.WriteLine();
    foreach (var detail in verdict.Details)
        Console.WriteLine($"  {detail}");
    return 0;
}

static int RunScreen(Dictionary<string, string> opts, AnalysisService analysis, IPluginCatalog catalog)
{
    ReportPlugins(catalog);
    var rows = analysis.Screen(opts.GetValueOrDefault("interval", "1d"));
    if (rows.Count == 0)
    {
        Console.WriteLine("No saved datasets to screen. Fetch some tickers first.");
        return 0;
    }

    var keys = rows[0].Reports.Select(r => r.Key).ToList();
    Console.WriteLine();
    Console.WriteLine($"{"Ticker",-8} " + string.Join(" ", keys.Select(k => $"{k,-20}")));
    foreach (var row in rows)
        Console.WriteLine($"{row.Ticker,-8} " + string.Join(" ", row.Reports.Select(r =>
            $"{$"{Icon(r.Signal.Direction)} {r.Signal.Direction} {r.Signal.Confidence:0}%",-20}")));
    return 0;
}

static int RunBacktest(List<string> rest, Dictionary<string, string> opts,
    AnalysisService analysis, IPluginCatalog catalog)
{
    if (rest.Count == 0) return Usage("backtest needs a ticker, e.g.: backtest AAPL");

    ReportPlugins(catalog);
    var interval = opts.GetValueOrDefault("interval", "1d");
    var options = new BacktestOptions(
        Warmup: int.Parse(opts.GetValueOrDefault("warmup", "60"), CultureInfo.InvariantCulture),
        Horizon: int.Parse(opts.GetValueOrDefault("horizon", "10"), CultureInfo.InvariantCulture),
        Step: int.Parse(opts.GetValueOrDefault("step", "5"), CultureInfo.InvariantCulture));
    var keys = opts.TryGetValue("agents", out var filter)
        ? filter.Split(',', StringSplitOptions.TrimEntries)
        : null;
    var reports = analysis.Backtest(rest[0], interval, options, keys);

    Console.WriteLine(
        $"Walk-forward backtest of {rest[0].ToUpperInvariant()} ({interval}): " +
        $"warmup {options.Warmup}, horizon {options.Horizon}, step {options.Step}");
    Console.WriteLine();
    Console.WriteLine($"{"Agent",-16} {"Checks",6} {"Calls",6} {"Hit rate",9} {"Avg move",9} {"Cum move",9}");
    foreach (var r in reports)
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{r.AgentKey,-16} {r.Checkpoints,6} {r.Directional,6} " +
            $"{(r.Directional == 0 ? "-" : r.HitRate.ToString("P0", CultureInfo.InvariantCulture)),9} " +
            $"{(r.Directional == 0 ? "-" : r.AvgAlignedReturn.ToString("+0.00%;-0.00%", CultureInfo.InvariantCulture)),9} " +
            $"{(r.Directional == 0 ? "-" : r.CumulativeAlignedReturn.ToString("+0.0%;-0.0%", CultureInfo.InvariantCulture)),9}"));
    Console.WriteLine();
    Console.WriteLine("Calls = directional verdicts; moves are direction-aligned forward returns per call.");
    return 0;
}

static async Task<int> RunWatchAsync(List<string> tickers, Dictionary<string, string> opts,
    WatchService watch, IPluginCatalog catalog)
{
    ReportPlugins(catalog);
    var interval = opts.GetValueOrDefault("interval", "1d");
    bool refresh = opts.GetValueOrDefault("fetch", "false") == "true";

    var result = await watch.RunAsync(tickers, interval, refresh);
    foreach (var warning in result.Warnings)
        Console.Error.WriteLine(warning);
    foreach (var change in result.Changes)
        Console.WriteLine(
            $"CHANGED  {change.Ticker,-8} {change.AgentDisplayName,-16} {change.From} -> {change.To} ({change.Confidence:0}%)");
    Console.WriteLine(result.Changes.Count == 0
        ? $"No verdict changes across {result.TickerCount} ticker(s)."
        : $"{result.Changes.Count} verdict change(s) across {result.TickerCount} ticker(s).");
    Console.WriteLine("Tip: schedule this command (cron / Task Scheduler) with --fetch to get a daily change report.");
    return 0;
}

static int RunReport(List<string> rest, Dictionary<string, string> opts, ReportService reports, MarketDataService market)
{
    var interval = opts.GetValueOrDefault("interval", "1d");
    double amount = double.Parse(opts.GetValueOrDefault("amount", "10000"), CultureInfo.InvariantCulture);

    if (rest.Count > 0)
    {
        PrintFullReport(reports.Generate(rest[0], interval, amount));
        return 0;
    }

    // Packet mode: a condensed morning briefing across every daily dataset.
    var tickers = market.ListDatasets().Where(d => d.Interval == "1d").Select(d => d.Ticker).ToList();
    if (tickers.Count == 0)
    {
        Console.WriteLine("No daily datasets stored. Fetch some tickers first.");
        return 0;
    }
    Console.WriteLine($"MORNING PACKET — {tickers.Count} tickers");
    Console.WriteLine(new string('=', 72));
    foreach (var ticker in tickers)
    {
        var r = reports.Generate(ticker, "1d", amount);
        var pieces = new List<string> { $"{Icon(r.Verdict.Direction)} {r.Verdict.Headline}" };
        if (r.Dividends.YieldPercent is { } dividendYield)
            pieces.Add(string.Create(CultureInfo.InvariantCulture, $"yield {dividendYield:0.0}%"));
        if (r.Social.PostCount > 0 && r.Social.BullishRatioPercent is { } crowd)
            pieces.Add(string.Create(CultureInfo.InvariantCulture, $"crowd {crowd:0}% bullish ({r.Social.PostCount} posts)"));
        var best = r.Backtests.FirstOrDefault(b => b.Directional > 0);
        if (best is not null)
            pieces.Add(string.Create(CultureInfo.InvariantCulture, $"best agent {best.AgentKey} {best.HitRate:P0}"));
        Console.WriteLine("  " + string.Join("  |  ", pieces));
    }
    Console.WriteLine();
    Console.WriteLine("Deep dive: candlesharp report <TICKER>");
    return 0;
}

static int RunSnapshot(List<string> tickers, Dictionary<string, string> opts,
    PatternService patterns, IPluginCatalog catalog)
{
    ReportPlugins(catalog);
    var interval = opts.GetValueOrDefault("interval", "1d");
    var results = tickers.Count == 0
        ? patterns.RecordAll(opts.TryGetValue("interval", out var only) ? only : null)
        : tickers.Select(t => patterns.Record(t, interval)).ToList();
    if (results.Count == 0)
    {
        Console.WriteLine("No saved datasets to snapshot. Fetch some tickers first.");
        return 0;
    }
    foreach (var r in results)
        Console.WriteLine($"{r.Ticker,-8} {r.Interval,-4} {r.Bars,6} bars ({r.Added} new), {r.Indicators} indicators -> {r.Path}");
    return 0;
}

static int RunPatterns(List<string> tickers, Dictionary<string, string> opts,
    PatternService patterns, IPluginCatalog catalog)
{
    ReportPlugins(catalog);
    var interval = opts.GetValueOrDefault("interval", "1d");
    var options = new PatternOptions(
        Horizon: int.Parse(opts.GetValueOrDefault("horizon", "10"), CultureInfo.InvariantCulture),
        MinSamples: int.Parse(opts.GetValueOrDefault("min-samples", "30"), CultureInfo.InvariantCulture),
        TestFraction: double.Parse(opts.GetValueOrDefault("test", "0.3"), CultureInfo.InvariantCulture),
        Buckets: int.Parse(opts.GetValueOrDefault("buckets", "5"), CultureInfo.InvariantCulture),
        Top: int.Parse(opts.GetValueOrDefault("top", "10"), CultureInfo.InvariantCulture),
        IncludeCombos: opts.GetValueOrDefault("no-combos", "false") != "true");
    var r = patterns.Analyze(tickers, interval, options);

    Console.WriteLine();
    Console.WriteLine($"Indicator patterns vs {options.Horizon}-bar forward return — {string.Join(", ", r.Tickers)} ({interval})");
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"Discovery: {r.Baseline.Samples} bars, baseline {Pct(r.Baseline.AvgReturn)} avg, {r.Baseline.WinRate * 100:0}% up. " +
        $"Held-out check: {r.OosBaseline.Samples} bars, baseline {Pct(r.OosBaseline.AvgReturn)} avg, {r.OosBaseline.WinRate * 100:0}% up."));
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{r.Tested} conditions with >= {options.MinSamples} samples; {r.Notable} notable (|t| >= 2) vs ~{r.ChanceExpected:0} expected by chance; " +
        $"{r.NotableHeld} of {r.NotableChecked} checkable notable patterns kept their direction on held-out data."));

    if (r.Excluded.Count > 0)
        Console.WriteLine($"Excluded (uses future bars): {string.Join(", ", r.Excluded)}");

    PrintPatternTable("BULLISH EDGES (stock did better than usual after these)", r.BullishEdges);
    PrintPatternTable("BEARISH EDGES (stock did worse than usual after these)", r.BearishEdges);
    PrintPatternTable("ACTIVE ON THE LATEST BAR", r.ActiveNow, showActive: r.Tickers.Count > 1);

    Console.WriteLine();
    Console.WriteLine("INDICATOR VALUES VS FORWARD RETURN (rank correlation; bucket = avg forward return, low -> high)");
    foreach (var study in r.ColumnStudies.OrderByDescending(c => Math.Abs(c.Correlation ?? 0)))
    {
        var name = study.RelativeToClose ? $"{study.Column} vs close" : study.Column;
        var buckets = string.Join(" ", study.Buckets.Select(b => b.Samples == 0
            ? "     -"
            : Pct(b.AvgReturn, 1).PadLeft(6)));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  {name,-26} rho {Corr(study.Correlation),6} (held-out {Corr(study.OosCorrelation),6})  {buckets}"));
    }

    Console.WriteLine();
    Console.WriteLine("Edge = avg forward return minus baseline; t counts only non-overlapping windows. Held = same edge sign on");
    Console.WriteLine("the held-out recent data. Many conditions are tested, so trust patterns that are notable AND held.");
    Console.WriteLine("Historical pattern statistics — not financial advice.");
    return 0;

    static string Corr(double? v) => v is { } x ? x.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) : "n/a";
}

static void PrintPatternTable(string title, IReadOnlyList<PatternStat> rows, bool showActive = false)
{
    Console.WriteLine();
    Console.WriteLine(title);
    if (rows.Count == 0)
    {
        Console.WriteLine("  (none with enough samples)");
        return;
    }
    Console.WriteLine($"  {"Condition",-58} {"N",5} {"Avg",7} {"Win",5} {"Edge",7} {"t",5}  {"Held-out",-18}");
    foreach (var p in rows)
    {
        var label = p.Label.Length > 58 ? p.Label[..55] + "..." : p.Label;
        var oos = p.HoldsOutOfSample is { } holds
            ? string.Create(CultureInfo.InvariantCulture, $"{(holds ? "held" : "FAILED")} {Pct(p.OosEdge!.Value)} n={p.OosSamples}")
            : $"n/a (n={p.OosSamples})";
        var active = showActive ? $"  now: {string.Join(",", p.ActiveIn)}" : "";
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  {label,-58} {p.Samples,5} {Pct(p.AvgReturn),7} {$"{p.WinRate * 100:0}%",5} {Pct(p.Edge),7} {Math.Round(p.TScore, 1),5:+0.0;-0.0;0.0}  {oos}{active}"));
    }
}

// Rounds before formatting so tiny negatives don't render as "-+0.00%".
static string Pct(double value, int decimals = 2)
{
    var digits = new string('0', decimals);
    return Math.Round(value, decimals + 2).ToString($"+0.{digits}%;-0.{digits}%;0.{digits}%", CultureInfo.InvariantCulture);
}

static int RunMLGuidance(List<string> rest, Dictionary<string, string> opts, MLGuidanceService ml)
{
    if (rest.Count == 0) return Usage("ml needs a ticker, e.g.: ml AAPL --horizon 10");

    var options = new MLGuidanceOptions(
        Horizon: int.Parse(opts.GetValueOrDefault("horizon", "10"), CultureInfo.InvariantCulture),
        Warmup: int.Parse(opts.GetValueOrDefault("warmup", "250"), CultureInfo.InvariantCulture),
        Step: int.Parse(opts.GetValueOrDefault("step", "25"), CultureInfo.InvariantCulture),
        NumTrees: int.Parse(opts.GetValueOrDefault("trees", "20"), CultureInfo.InvariantCulture),
        MaxDepth: int.Parse(opts.GetValueOrDefault("depth", "3"), CultureInfo.InvariantCulture));
    var r = ml.Analyze(rest[0], opts.GetValueOrDefault("interval", "1d"), options);

    Console.WriteLine($"ML guidance for {r.Ticker} ({options.Horizon}-bar horizon), trained on {r.FeatureCount} features as of {r.AsOf:yyyy-MM-dd}");
    if (r.Excluded.Count > 0)
        Console.WriteLine($"Excluded (uses future bars): {string.Join(", ", r.Excluded)}");
    if (r.EffectiveWarmup < r.Options.Warmup)
        Console.WriteLine($"Warm-up reduced to {r.EffectiveWarmup} bars (from {r.Options.Warmup}) to fit this ticker's available history.");
    Console.WriteLine();
    Console.WriteLine($"{Icon(r.Direction)} {r.Direction}  probability of higher in {options.Horizon} bars: {r.Probability * 100:0.0}%  (confidence {r.Confidence:0}%)");
    Console.WriteLine();
    Console.WriteLine("Top features:");
    foreach (var f in r.TopFeatures.Take(8))
        Console.WriteLine($"  {f.Name,-26} {f.Importance * 100,5:0.0}%");
    Console.WriteLine();
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"Walk-forward check: {r.Checkpoints} checkpoints, {r.Directional} directional, {r.HitRate * 100:0}% right vs a {r.BaselineHitRate * 100:0}% naive baseline; " +
        $"avg move when right direction {r.AvgAlignedReturn * 100:+0.00;-0.00}%, cumulative {r.CumulativeAlignedReturn * 100:+0.0;-0.0}%."));
    if (r.Calibration.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Calibration (predicted probability vs how often the stock was actually up):");
        foreach (var c in r.Calibration)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {c.Range,-8} n={c.Samples,4}  predicted~{c.PredictedAvg * 100:0}%  actual~{c.ActualUpRate * 100:0}%"));
    }
    Console.WriteLine();
    Console.WriteLine("A model trained on one stock's own noisy history — treat it as one more opinion, not a signal. Not financial advice.");
    return 0;
}

static int RunRules(List<string> rest, Dictionary<string, string> opts, RuleService rules)
{
    var interval = opts.GetValueOrDefault("interval", "1d");
    if (rules.List().Count == 0)
    {
        Console.WriteLine("No rules saved yet. Add one, e.g.:");
        Console.WriteLine("  candlesharp rule add AAPL buy \"RSI_14 < 30\" \"Close > SMA_50\" --name \"Dip in uptrend\"");
        return 0;
    }

    var results = rest.Count > 0 ? rules.Evaluate(rest[0], interval) : rules.EvaluateAll(interval);
    if (results.Count == 0)
    {
        Console.WriteLine(rest.Count > 0 ? $"No rules apply to {rest[0].ToUpperInvariant()}." : "No rules apply to any saved dataset.");
        return 0;
    }

    var firing = results.Where(r => r.FiringNow).ToList();
    Console.WriteLine(firing.Count == 0
        ? "No rule is firing on the latest bar."
        : $"{firing.Count} rule(s) firing on the latest bar:");
    foreach (var r in firing)
        Console.WriteLine($"  {(r.Rule.Action == RuleAction.Buy ? "▲ BUY " : "▼ SELL")} {r.Ticker,-8} {r.Rule.Name}{(r.NewSignal ? "  (new today)" : "")}");

    Console.WriteLine();
    foreach (var r in results.OrderByDescending(r => r.FiringNow).ThenBy(r => r.Ticker))
    {
        var status = r.Error is not null ? "ERROR"
            : r.FiringNow ? (r.Rule.Action == RuleAction.Buy ? "BUY NOW" : "SELL NOW")
            : "waiting";
        Console.WriteLine($"[{r.Rule.Id}] {r.Ticker,-8} {r.Rule.Action.ToString().ToUpperInvariant(),-4} {status,-8} {r.Rule.Name}");
        Console.WriteLine($"    when {string.Join(" AND ", r.Rule.Conditions)}");
        if (r.Error is not null)
            Console.WriteLine($"    {r.Error}");
        else if (r.Signals.Count == 0)
            Console.WriteLine("    never fired in the stored history");
        else if (r.Scored == 0)
            Console.WriteLine($"    fired {r.Signals.Count} time(s), last {r.LastSignal:yyyy-MM-dd}; not enough bars since to score");
        else
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"    fired {r.Signals.Count} time(s), last {r.LastSignal:yyyy-MM-dd}; {r.HitRate * 100:0}% right after {r.Rule.Horizon} bars, " +
                $"avg move {Pct(r.AvgReturn!.Value)}, edge {Pct(r.Edge!.Value)} vs a typical stretch{(r.Scored < 10 ? " (few signals)" : "")}"));
    }
    Console.WriteLine();
    Console.WriteLine("Edge = how much better than an average bar each signal did in the rule's direction. Not financial advice.");
    return 0;
}

static int RunRule(List<string> rest, Dictionary<string, string> opts, RuleService rules)
{
    var interval = opts.GetValueOrDefault("interval", "1d");
    switch (rest.FirstOrDefault())
    {
        case "add" when rest.Count >= 4:
            if (!Enum.TryParse<RuleAction>(rest[2], ignoreCase: true, out var action))
                return Usage("rule add needs buy or sell, e.g.: rule add AAPL buy \"RSI_14 < 30\"");
            var rule = rules.Add(opts.TryGetValue("name", out var ruleName) ? ruleName : null, rest[1], action, rest.Skip(3).ToList(),
                int.Parse(opts.GetValueOrDefault("horizon", "10"), CultureInfo.InvariantCulture), interval);
            Console.WriteLine($"Saved rule {rule.Id}: {rule.Action.ToString().ToUpperInvariant()} {rule.Ticker} when {string.Join(" AND ", rule.Conditions)}");
            Console.WriteLine($"Check it with: candlesharp rules{(rule.Ticker == TradeRule.AnyTicker ? "" : " " + rule.Ticker)}");
            return 0;
        case "remove" when rest.Count == 2:
            if (!rules.Remove(rest[1]))
            {
                Console.Error.WriteLine($"No rule with id {rest[1]} (see: candlesharp rules)");
                return 1;
            }
            Console.WriteLine($"Removed rule {rest[1]}.");
            return 0;
        case "suggest" when rest.Count == 2:
            return RunRuleSuggest(rest[1], interval, opts, rules);
        case "fields" when rest.Count == 2:
            var operands = rules.Operands(rest[1], interval);
            Console.WriteLine("Values (compare with <, <=, >, >=, crosses above, crosses below):");
            Console.WriteLine("  " + string.Join(", ", operands.Values));
            Console.WriteLine("Signals (use with: is Bullish / is Bearish / turns Bullish / turns Bearish):");
            Console.WriteLine("  " + string.Join(", ", operands.Stances));
            Console.WriteLine("Add \"vs close\" to a price level to compare its distance from the close, e.g. \"SMA_50 vs close < -5%\".");
            return 0;
        default:
            return Usage("rule needs: add <TICKER|*> <buy|sell> \"<condition>\"..., suggest <TICKER>, remove <id>, or fields <TICKER>");
    }
}

static int RunRuleSuggest(string ticker, string interval, Dictionary<string, string> opts, RuleService rules)
{
    var options = new SuggestOptions(
        Horizon: int.Parse(opts.GetValueOrDefault("horizon", "10"), CultureInfo.InvariantCulture),
        MinSignals: int.Parse(opts.GetValueOrDefault("min-signals", "8"), CultureInfo.InvariantCulture),
        Top: int.Parse(opts.GetValueOrDefault("top", "5"), CultureInfo.InvariantCulture));
    var r = rules.Suggest(ticker, interval, options);

    Console.WriteLine($"Suggested rules for {r.Ticker} ({interval}), from its own past {options.Horizon}-bar performance");
    Console.WriteLine($"Chosen from {r.Tested} candidate rules on history up to {r.DiscoveryEnd:yyyy-MM-dd}, then re-checked on the recent part it never saw.");
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"With that many tried, a t around {r.LuckBar:0.0} can happen by luck: STRONG = beats that AND held up clearly on recent data."));
    if (r.Buy.Concat(r.Sell).All(s => s.Strength == SuggestionStrength.Weak))
        Console.WriteLine("No strong rules: this stock's past doesn't show patterns that clearly beat luck. The weak ones below may be noise.");
    foreach (var (title, list) in new[] { ("BUY RULES", r.Buy), ("SELL RULES", r.Sell) })
    {
        Console.WriteLine();
        Console.WriteLine(title);
        if (list.Count == 0)
        {
            Console.WriteLine("  (none held up — try another --horizon, or more history)");
            continue;
        }
        foreach (var s in list)
        {
            var check = s.HeldUp switch
            {
                true => string.Create(CultureInfo.InvariantCulture,
                    $"recent: held up ({s.Recent!.Signals} signals, {s.Recent.HitRate * 100:0}% right, edge {Pct(s.Recent.Edge)})"),
                _ => $"recent: too few signals to check ({s.Recent?.Signals ?? 0})",
            };
            Console.WriteLine($"  [{s.Strength.ToString().ToUpperInvariant()}] {(s.FiringNow ? "FIRING NOW  " : "")}when {string.Join(" AND ", s.Rule.Conditions)}");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"    past: {s.Discovery.Signals} signals, {s.Discovery.HitRate * 100:0}% right, avg move {Pct(s.Discovery.AvgReturn)}, edge {Pct(s.Discovery.Edge)}, t {s.Discovery.TScore:0.0}; {check}"));
            Console.WriteLine($"    save: candlesharp rule add {r.Ticker} {s.Rule.Action.ToString().ToLowerInvariant()} " +
                string.Join(" ", s.Rule.Conditions.Select(c => $"\"{c}\"")) +
                (options.Horizon != 10 ? $" --horizon {options.Horizon}" : ""));
        }
    }
    Console.WriteLine();
    Console.WriteLine("Found by searching many rules, so some luck is baked in even after the recent check. Not financial advice.");
    return 0;
}

static void PrintFullReport(TickerReport r)
{
    var line = new string('=', 72);
    Console.WriteLine(line);
    Console.WriteLine($"CANDLESHARP RESEARCH REPORT — {r.Ticker} ({r.Interval})");
    Console.WriteLine(line);
    Console.WriteLine();
    Console.WriteLine($"{Icon(r.Verdict.Direction)} {r.Verdict.Headline}");
    foreach (var detail in r.Verdict.Details)
        Console.WriteLine($"  {detail}");

    Console.WriteLine();
    Console.WriteLine("ANALYSTS");
    foreach (var agent in r.Agents.OrderByDescending(a => a.Signal.Confidence))
        Console.WriteLine($"  {Icon(agent.Signal.Direction)} {agent.DisplayName,-16} {agent.Signal.Direction,-8} {agent.Signal.Confidence,3:0}%  {agent.Signal.Reasoning}");

    Console.WriteLine();
    Console.WriteLine("BACKTEST (walk-forward — how often each agent was right on this ticker)");
    foreach (var b in r.Backtests)
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  {b.AgentKey,-16} {b.Directional,3} calls  " +
            $"{(b.Directional == 0 ? "   -" : b.HitRate.ToString("P0", CultureInfo.InvariantCulture)),5} hit  " +
            $"{(b.Directional == 0 ? "-" : b.CumulativeAlignedReturn.ToString("+0.0%;-0.0%", CultureInfo.InvariantCulture)),8} cum"));

    Console.WriteLine();
    Console.WriteLine("DIVIDENDS");
    if (r.Dividends.PaymentCount == 0)
        Console.WriteLine("  No dividend history stored.");
    else
    {
        var dividendLine = string.Create(CultureInfo.InvariantCulture,
            $"  Trailing year {r.Dividends.TrailingYearTotal:0.####}/share");
        if (r.Dividends.YieldPercent is { } y)
            dividendLine += string.Create(CultureInfo.InvariantCulture, $" (yield {y:0.0}%)");
        dividendLine += $", {r.Dividends.ConsecutiveGrowthYears} consecutive growth year(s)";
        Console.WriteLine(dividendLine);
        if (r.Dividends.GrowthYoYPercent is { } g1)
        {
            var growthLine = string.Create(CultureInfo.InvariantCulture, $"  Growth {g1:+0.0;-0.0}% YoY");
            if (r.Dividends.Growth3YPercent is { } g3)
                growthLine += string.Create(CultureInfo.InvariantCulture, $", {g3:+0.0;-0.0}%/yr over 3y");
            if (r.Dividends.Growth5YPercent is { } g5)
                growthLine += string.Create(CultureInfo.InvariantCulture, $", {g5:+0.0;-0.0}%/yr over 5y");
            Console.WriteLine(growthLine);
        }
    }

    Console.WriteLine();
    Console.WriteLine("CROWD");
    if (r.Social.PostCount == 0)
        Console.WriteLine("  No chatter stored (refresh pulls it automatically).");
    else
    {
        var crowdLine = $"  {r.Social.PostCount} posts, ";
        crowdLine += r.Social.BullishRatioPercent is { } ratio
            ? string.Create(CultureInfo.InvariantCulture, $"{ratio:0}% bullish")
            : "no directional reads";
        crowdLine += string.Create(CultureInfo.InvariantCulture,
            $" (tagged {r.Social.TaggedBullish}/{r.Social.TaggedBearish}, lexicon {r.Social.LexiconBullish}/{r.Social.LexiconBearish}), newest {r.Social.NewestPost:yyyy-MM-dd}");
        Console.WriteLine(crowdLine);
    }

    Console.WriteLine();
    Console.WriteLine("ONE-YEAR BUY & HOLD");
    if (r.Simulation is not { } sim)
        Console.WriteLine("  Not enough daily history for a one-year simulation.");
    else
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  {sim.Invested:0} on {sim.BuyDate:yyyy-MM-dd} -> {sim.EndTotalValue:0.00} ({sim.TotalReturnPercent:+0.0;-0.0}%) " +
            $"= price {sim.PriceReturnPercent:+0.0;-0.0}% + dividends {sim.DividendYieldOnCostPercent:+0.0;-0.0}% " +
            $"({sim.DividendCash:0.00} cash over {sim.DividendPayments} payments)"));

    Console.WriteLine();
    Console.WriteLine("Automated technical read of stored data only — not financial advice.");
}

static void ReportPlugins(IPluginCatalog catalog)
{
    foreach (var group in catalog.Indicators.GroupBy(r => r.Source))
        Console.WriteLine($"Loaded {group.Count()} indicator(s) from {group.Key}");
    if (catalog.Indicators.Count == 0)
    {
        var scanned = string.Join(", ", catalog.ScannedDirectories.Select(Path.GetFullPath));
        Console.Error.WriteLine(
            $"warning: no indicator plugin DLLs found (scanned: {scanned}) — build the solution first, or pass --plugins <dir>");
    }
}

static string Icon(SignalDirection direction) => direction switch
{
    SignalDirection.Bullish => "▲",
    SignalDirection.Bearish => "▼",
    _ => "▬",
};

static void WriteIndicatorsCsv(
    string path, IReadOnlyList<Candle> candles,
    IReadOnlyList<(string Name, double?[] Values)> columns)
{
    using var writer = new StreamWriter(path);
    writer.WriteLine("Date,Open,High,Low,Close,Volume," + string.Join(',', columns.Select(c => c.Name)));
    for (int i = 0; i < candles.Count; i++)
    {
        var c = candles[i];
        var cells = new List<string>
        {
            c.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss"),
            Num(c.Open), Num(c.High), Num(c.Low), Num(c.Close),
            c.Volume.ToString(CultureInfo.InvariantCulture),
        };
        cells.AddRange(columns.Select(col => col.Values[i] is { } v ? Num(v) : ""));
        writer.WriteLine(string.Join(',', cells));
    }

    static string Num(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}

static void PrintTail(
    IReadOnlyList<Candle> candles,
    IReadOnlyList<(string Name, double?[] Values)> columns, int tail)
{
    var shown = new[] { "SMA_20", "RSI_14", "MACD", "BB_upper_20", "BB_lower_20", "ATR_14" };
    var cols = columns.Where(c => shown.Contains(c.Name)).ToList();

    Console.WriteLine();
    Console.WriteLine($"{"Date",-12}{"Close",10}" +
        string.Concat(cols.Select(c => $"{c.Name,13}")));
    for (int i = Math.Max(0, candles.Count - tail); i < candles.Count; i++)
    {
        Console.WriteLine($"{candles[i].Timestamp,-12:yyyy-MM-dd}{candles[i].Close,10:F2}" +
            string.Concat(cols.Select(c => c.Values[i] is { } v
                ? $"{v,13:F2}"
                : $"{"-",13}")));
    }
}

static Dictionary<string, string> ParseOptions(string[] args, out List<string> positional)
{
    var opts = new Dictionary<string, string>();
    positional = [];
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith("--"))
        {
            var name = args[i][2..];
            // A trailing option or one followed by another --option is a boolean flag.
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
                opts[name] = "true";
            else
                opts[name] = args[++i];
        }
        else
        {
            positional.Add(args[i]);
        }
    }
    return opts;
}

static DateOnly ParseDate(string s) =>
    DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);

static int Usage(string? error = null)
{
    if (error is not null) Console.Error.WriteLine($"Error: {error}\n");
    Console.WriteLine("""
        CandleSharp (EricoreTech) - pull free stock data from Yahoo Finance, store it locally,
        and compute technical indicators.

        Usage:
          candlesharp fetch <TICKER>... [--period 1y] [--interval 1d]
                                        [--start YYYY-MM-DD] [--end YYYY-MM-DD]
          candlesharp list
          candlesharp refresh [--period 3mo]
          candlesharp indicators <TICKER> [--interval 1d] [--tail 10]
          candlesharp signals <TICKER> [--interval 1d] [--tail 10]
          candlesharp resample <TICKER> [--interval 1d] --to <1wk|1mo>
          candlesharp dividends <TICKER>
          candlesharp simulate <TICKER> [--amount 10000] [--start YYYY-MM-DD] [--end YYYY-MM-DD]
          candlesharp social <TICKER> [--fetch]
          candlesharp analyze <TICKER> [--interval 1d] [--agents key1,key2]
          candlesharp verdict <TICKER> [--interval 1d]
          candlesharp screen [--interval 1d]
          candlesharp watch [TICKER...] [--interval 1d] [--fetch]
          candlesharp backtest <TICKER> [--interval 1d] [--warmup 60] [--horizon 10] [--step 5]
          candlesharp report [TICKER] [--interval 1d] [--amount 10000]
          candlesharp snapshot [TICKER...] [--interval 1d]
          candlesharp patterns [TICKER...] [--interval 1d] [--horizon 10] [--min-samples 30]
                               [--test 0.3] [--buckets 5] [--top 10] [--no-combos]
          candlesharp rules [TICKER] [--interval 1d]
          candlesharp rule add <TICKER|*> <buy|sell> "<condition>"... [--name "..."] [--horizon 10]
          candlesharp rule remove <ID>
          candlesharp rule suggest <TICKER> [--horizon 10] [--min-signals 8] [--top 5]
          candlesharp rule fields <TICKER>
          candlesharp ml <TICKER> [--horizon 10] [--warmup 250] [--step 25] [--trees 20] [--depth 3]

        Global options:
          --data-dir <dir>   Directory for local CSV files (default: ./data)
          --plugins <dir>    Directory scanned for indicator plugin DLLs (default: ./plugins)

        Examples:
          candlesharp fetch AAPL MSFT SPY --period 2y
          candlesharp fetch SPY --interval 1h --period 1mo
          candlesharp fetch AAPL --start 2020-01-01 --end 2023-12-31
          candlesharp indicators AAPL
          candlesharp signals AAPL
          candlesharp verdict AAPL
          candlesharp patterns AAPL --horizon 20
          candlesharp rule add AAPL buy "RSI_14 < 30" "Close > SMA_50" --name "Dip in uptrend"
        """);
    return error is null ? 0 : 2;
}
