using EricoreTech.CandleSharp.Application;
using EricoreTech.CandleSharp.Blazor.Components;
using EricoreTech.CandleSharp.Blazor.Services;
using EricoreTech.CandleSharp.Infrastructure;

// data/ and plugins/ stay relative to wherever the app is launched from, matching the CLI.
var builder = WebApplication.CreateBuilder(args);

var dataDir = builder.Configuration["data-dir"] ?? "data";
var pluginsDir = builder.Configuration["plugins"] ?? "plugins";

builder.Services.AddSingleton(new CsvCandleRepository(dataDir));
builder.Services.AddSingleton<ICandleRepository>(sp => sp.GetRequiredService<CsvCandleRepository>());
builder.Services.AddSingleton<IDividendRepository>(sp => sp.GetRequiredService<CsvCandleRepository>());
builder.Services.AddSingleton<IQuoteFeed, YahooFinanceClient>();
builder.Services.AddSingleton<ISocialFeed, StockTwitsClient>();
builder.Services.AddSingleton<ISocialRepository>(new JsonSocialStore(dataDir));
builder.Services.AddSingleton<IAgentStateStore>(new JsonAgentStateStore(dataDir));
builder.Services.AddSingleton<ISnapshotRepository>(new CsvSnapshotRepository(dataDir));
builder.Services.AddSingleton<IRuleRepository>(new JsonRuleStore(dataDir));
builder.Services.AddSingleton<IPluginCatalog>(sp => new PluginCatalog(pluginsDir,
    error => sp.GetRequiredService<ILoggerFactory>().CreateLogger("Plugins")
        .LogWarning("plugin warning: {Error}", error)));
builder.Services.AddSingleton<MarketDataService>();
builder.Services.AddSingleton<AnalysisService>();
builder.Services.AddSingleton<SimulationService>();
builder.Services.AddSingleton<SocialService>();
builder.Services.AddSingleton<ReportService>();
builder.Services.AddSingleton<WatchService>();
builder.Services.AddSingleton<PatternService>();
builder.Services.AddSingleton<RuleService>();
builder.Services.AddSingleton<MLGuidanceService>();
builder.Services.AddSingleton<WatchlistService>();

// The one piece of state shared across a browser tab's circuit: which ticker is selected.
builder.Services.AddScoped<DashboardState>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
