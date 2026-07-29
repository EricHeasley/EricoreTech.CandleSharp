using EricoreTech.CandleSharp.Application;
using EricoreTech.CandleSharp.Domain;
using EricoreTech.CandleSharp.Infrastructure;
using EricoreTech.CandleSharp.Web;

// Web root rides with the binary so the server can be launched from any
// working directory (data/ and plugins/ stay cwd-relative on purpose).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
});

// Both directories are relative to where the server is launched, matching the CLI.
var dataDir = builder.Configuration["data-dir"] ?? "data";
var pluginsDir = builder.Configuration["plugins"] ?? "plugins";

builder.Services.AddSingleton(new CsvCandleRepository(dataDir));
builder.Services.AddSingleton<ICandleRepository>(sp => sp.GetRequiredService<CsvCandleRepository>());
builder.Services.AddSingleton<IDividendRepository>(sp => sp.GetRequiredService<CsvCandleRepository>());
builder.Services.AddSingleton<IQuoteFeed, YahooFinanceClient>();
builder.Services.AddSingleton<ISocialFeed, StockTwitsClient>();
builder.Services.AddSingleton<ISocialRepository>(new JsonSocialStore(dataDir));
builder.Services.AddSingleton<IAgentStateStore>(new JsonAgentStateStore(dataDir));
builder.Services.AddSingleton<IPluginCatalog>(sp => new PluginCatalog(pluginsDir,
    error => sp.GetRequiredService<ILoggerFactory>().CreateLogger("Plugins")
        .LogWarning("plugin warning: {Error}", error)));
builder.Services.AddSingleton<MarketDataService>();
builder.Services.AddSingleton<AnalysisService>();
builder.Services.AddSingleton<SimulationService>();
builder.Services.AddSingleton<SocialService>();
builder.Services.AddSingleton<ReportService>();
builder.Services.AddSingleton<WatchService>();

var app = builder.Build();

var catalog = app.Services.GetRequiredService<IPluginCatalog>();
foreach (var group in catalog.Indicators.GroupBy(r => r.Source))
    app.Logger.LogInformation("Loaded {Count} indicator(s) from {Source}", group.Count(), group.Key);
foreach (var group in catalog.Agents.GroupBy(a => a.Source))
    app.Logger.LogInformation("Loaded {Count} agent(s) from {Source}", group.Count(), group.Key);
if (catalog.Indicators.Count == 0)
    app.Logger.LogWarning(
        "No indicator plugin DLLs found (scanned: {Scanned}) — build the solution first, or pass --plugins <dir>",
        string.Join(", ", catalog.ScannedDirectories.Select(Path.GetFullPath)));

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/indicators", (IPluginCatalog plugins) =>
    plugins.Indicators.Select(r => new { name = r.Indicator.Name, source = r.Source }));

app.MapGet("/api/datasets", (MarketDataService market, ICandleRepository repository) =>
    market.ListDatasets().Select(dataset =>
    {
        var candles = repository.Load(dataset.Ticker, dataset.Interval);
        return new
        {
            ticker = dataset.Ticker,
            interval = dataset.Interval,
            bars = candles.Count,
            start = candles[0].Timestamp,
            end = candles[^1].Timestamp,
        };
    }));

app.MapGet("/api/series/{ticker}", (string ticker, string? interval, AnalysisService analysis) =>
{
    SeriesAnalysis series;
    try
    {
        series = analysis.GetSeries(ticker, interval ?? "1d");
    }
    catch (FileNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }

    return Results.Ok(new
    {
        ticker = series.Ticker,
        interval = series.Interval,
        candles = series.Candles.Select(c => new
        {
            t = c.Timestamp,
            o = c.Open,
            h = c.High,
            l = c.Low,
            c = c.Close,
            v = c.Volume,
        }),
        columns = series.Signals.Columns.Select(col => new { name = col.Name, values = col.Values }),
        stances = series.Signals.Stances.ToDictionary(
            s => s.Key,
            s => s.Value.Select(d => d.ToString()).ToArray()),
        triggers = series.Signals.Triggers.Select(t => new
        {
            t = t.Timestamp,
            indicator = t.Indicator,
            direction = t.Direction.ToString(),
        }),
    });
});

app.MapGet("/api/analyze/{ticker}", (string ticker, string? interval, AnalysisService analysis) =>
{
    try
    {
        return Results.Ok(analysis.Analyze(ticker, interval ?? "1d").Select(AgentReportDto));
    }
    catch (FileNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

app.MapGet("/api/verdict/{ticker}", (string ticker, string? interval, AnalysisService analysis) =>
{
    try
    {
        var verdict = analysis.GetVerdict(ticker, interval ?? "1d");
        return Results.Ok(new
        {
            direction = verdict.Direction.ToString(),
            confidence = verdict.Confidence,
            headline = verdict.Headline,
            details = verdict.Details,
        });
    }
    catch (FileNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

app.MapGet("/api/dividends/{ticker}", (string ticker, MarketDataService market) =>
{
    var summary = market.GetDividendSummary(ticker);
    return Results.Ok(new
    {
        ticker = summary.Ticker,
        paymentCount = summary.PaymentCount,
        lastPaymentDate = summary.LastPaymentDate,
        lastAmount = summary.LastAmount,
        trailingYearTotal = summary.TrailingYearTotal,
        yieldPercent = summary.YieldPercent,
        growthYoYPercent = summary.GrowthYoYPercent,
        growth3YPercent = summary.Growth3YPercent,
        growth5YPercent = summary.Growth5YPercent,
        consecutiveGrowthYears = summary.ConsecutiveGrowthYears,
        annualTotals = summary.AnnualTotals.Select(a => new { year = a.Year, total = a.Total }),
        recentPayments = summary.RecentPayments.Select(d => new { date = d.Timestamp, amount = d.Amount }),
    });
});

app.MapGet("/api/simulate/{ticker}", (string ticker, double? amount, string? start, string? end, SimulationService simulation) =>
{
    try
    {
        var r = simulation.BuyAndHold(
            ticker, amount ?? 10_000,
            start is { } s ? DateTime.Parse(s) : null,
            end is { } e ? DateTime.Parse(e) : null);
        return Results.Ok(new
        {
            ticker = r.Ticker,
            buyDate = r.BuyDate,
            sellDate = r.SellDate,
            invested = r.Invested,
            shares = r.Shares,
            buyPrice = r.BuyPrice,
            sellPrice = r.SellPrice,
            endStockValue = r.EndStockValue,
            dividendCash = r.DividendCash,
            dividendPayments = r.DividendPayments,
            endTotalValue = r.EndTotalValue,
            totalReturnPercent = r.TotalReturnPercent,
            priceReturnPercent = r.PriceReturnPercent,
            dividendYieldOnCostPercent = r.DividendYieldOnCostPercent,
            reinvestedValue = r.ReinvestedValue,
            reinvestedReturnPercent = r.ReinvestedReturnPercent,
        });
    }
    catch (FileNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or FormatException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/social/{ticker}", (string ticker, SocialService social) =>
{
    var s = social.GetSummary(ticker);
    return Results.Ok(new
    {
        ticker = s.Ticker,
        postCount = s.PostCount,
        taggedBullish = s.TaggedBullish,
        taggedBearish = s.TaggedBearish,
        lexiconBullish = s.LexiconBullish,
        lexiconBearish = s.LexiconBearish,
        bullishRatioPercent = s.BullishRatioPercent,
        averageLexiconScore = s.AverageLexiconScore,
        oldestPost = s.OldestPost,
        newestPost = s.NewestPost,
        recentPosts = s.RecentPosts.Select(post => new
        {
            date = post.Timestamp,
            author = post.Author,
            text = post.Text,
            tagged = post.Tagged.ToString(),
        }),
    });
});

app.MapPost("/api/social/{ticker}/refresh", async (string ticker, SocialService social) =>
{
    try
    {
        int added = await social.FetchAndStoreAsync(ticker);
        return Results.Ok(new { pulled = added });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 502);
    }
});

app.MapGet("/api/screen", (AnalysisService analysis) =>
    analysis.Screen().Select(row => new
    {
        ticker = row.Ticker,
        interval = row.Interval,
        agents = row.Reports.Select(r => new
        {
            key = r.Key,
            displayName = r.DisplayName,
            direction = r.Signal.Direction.ToString(),
            confidence = r.Signal.Confidence,
        }),
    }));

app.MapGet("/report/{ticker}", (string ticker, string? interval, ReportService reports) =>
{
    try
    {
        return Results.Content(ReportHtml.Render(reports.Generate(ticker, interval ?? "1d")), "text/html");
    }
    catch (FileNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

app.MapPost("/api/refresh", async (MarketDataService market) =>
{
    var result = await market.RefreshAllAsync();
    return Results.Ok(new
    {
        refreshed = result.Refreshed.Select(r => new
        {
            ticker = r.Ticker,
            interval = r.Interval,
            bars = r.Bars,
            dividends = r.Dividends,
            mode = r.Mode,
        }),
        socialPulls = result.SocialPulls.Select(pull => new { ticker = pull.Ticker, posts = pull.Posts }),
        warnings = result.Warnings,
    });
});

app.MapPost("/api/fetch", async (FetchRequest request, MarketDataService market) =>
{
    if (string.IsNullOrWhiteSpace(request.Ticker))
        return Results.BadRequest(new { error = "ticker is required" });

    try
    {
        var result = await market.FetchAsync(
            request.Ticker, request.Period ?? "1y", request.Interval ?? "1d",
            request.Start is { } s ? DateOnly.Parse(s) : null,
            request.End is { } e ? DateOnly.Parse(e) : null);
        return Results.Ok(new
        {
            ticker = result.Ticker,
            interval = result.Interval,
            bars = result.Bars,
            path = result.Path,
            dividends = result.Dividends,
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 502);
    }
});

app.Run();

static object AgentReportDto(AgentReport report) => new
{
    key = report.Key,
    displayName = report.DisplayName,
    direction = report.Signal.Direction.ToString(),
    confidence = report.Signal.Confidence,
    reasoning = report.Signal.Reasoning,
    scores = report.Scores.Select(s => new
    {
        title = s.Title,
        score = s.Score,
        maxScore = s.MaxScore,
        details = s.Details,
    }),
};
