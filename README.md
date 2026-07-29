# EricoreTech.CandleSharp

A C# (.NET 8) app that pulls free stock data from Yahoo Finance (no API key
required), stores it locally as CSV, and runs technical indicators over it —
with a **plugin system** for indicators and a **web dashboard** for viewing
charts and signals. No external NuGet dependencies anywhere.

**Every indicator is a plugin** — including the ones that ship with the app.
Core contains no indicators at all, only the contract, the engine, and the
math toolkit; the Standard and Advanced packs are separate projects whose
DLLs are copied into `plugins/` at build time and discovered at startup,
exactly like a third-party plugin would be.

## Architecture (Clean Architecture)

```
EricoreTech.CandleSharp.sln
src/
  EricoreTech.CandleSharp.Domain/                pure logic, zero dependencies: entities, contracts,
                                                 indicator math, engine, resampler, verdict composer, backtester
  EricoreTech.CandleSharp.Application/           use-case services (MarketData, Analysis, Watch) and ports
                                                 (ICandleRepository, IQuoteFeed, IPluginCatalog, IAgentStateStore)
  EricoreTech.CandleSharp.Infrastructure/        adapters: CSV candle repository, Yahoo Finance client,
                                                 plugin catalog (reflection), JSON agent-state store
  EricoreTech.CandleSharp.Indicators.Standard/   indicator pack: SMA/EMA cross, RSI, MACD, Bollinger, ATR
  EricoreTech.CandleSharp.Indicators.Advanced/   indicator pack: Stochastic, ADX, Ichimoku, PSAR, SuperTrend, MFI, CCI, Donchian, OBV
  EricoreTech.CandleSharp.Indicators.Patterns/   indicator pack: candlestick patterns (Engulfing, Hammer, Marubozu)
  EricoreTech.CandleSharp.Agents/                agent pack: 8 trading agents
  EricoreTech.CandleSharp.Cli/                   thin presentation: parses args, calls Application services
  EricoreTech.CandleSharp.Web/                   thin presentation: DI-wired minimal API + static frontend
plugins/
  *.dll                              build output of all indicator/agent packs (gitignored)
tests/
  EricoreTech.CandleSharp.Tests/                 framework-free offline test suite
```

The dependency rule is enforced by project references: Domain has none,
Application references only Domain, Infrastructure implements Application's
ports, and the two presentation hosts are composition roots. Indicator and
agent packs reference Domain only, so plugins never touch IO or hosting
concerns.

Build the solution once (`dotnet build`) and the pack DLLs land in
`plugins/`; the CLI and web app load whatever is there on startup and report
what they found. Both hosts also build the packs automatically when run via
`dotnet run`.

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

## CLI

```bash
dotnet run --project src/EricoreTech.CandleSharp.Cli -- fetch AAPL MSFT SPY --period 2y
dotnet run --project src/EricoreTech.CandleSharp.Cli -- list
dotnet run --project src/EricoreTech.CandleSharp.Cli -- indicators AAPL     # writes CSV with all columns
dotnet run --project src/EricoreTech.CandleSharp.Cli -- signals AAPL        # stances + trigger history
```

Data lands in `./data/<TICKER>_<interval>.csv` (gitignored); re-fetching merges
new bars into the existing file. `--interval 1h`, `--start`/`--end`, and
`--data-dir` work as before; `--plugins <dir>` points at a plugin folder
(default `./plugins`).

Refresh everything you have in one shot — every saved dataset gets a
recent-window top-up (bars + dividends) merged into its history, resampled
weekly/monthly datasets are re-derived locally from the refreshed dailies,
and the latest social chatter is pulled once per ticker and merged into
local storage:

```bash
dotnet run --project src/EricoreTech.CandleSharp.Cli -- refresh
```

The dashboard has the same as a "Refresh all" button next to the fetch
form. Failures (offline, delisted ticker) are reported per dataset and
never touch the already-saved data.

Derive weekly or monthly series locally instead of re-downloading:

```bash
dotnet run --project src/EricoreTech.CandleSharp.Cli -- resample AAPL --to 1wk
dotnet run --project src/EricoreTech.CandleSharp.Cli -- signals AAPL --interval 1wk
```

## Web dashboard

```bash
dotnet run --project src/EricoreTech.CandleSharp.Web
```

Open http://localhost:5000. The dashboard shows a candlestick chart with
toggleable overlays (SMA/EMA/Bollinger), trigger markers on the bars, an RSI
panel, each indicator's current stance, and the recent trigger table — light
and dark theme both supported. You can fetch new tickers straight from the
page. It reads/writes the same `./data` directory as the CLI and loads the
same `./plugins` directory, so both frontends always agree.

API, if you want to script against it: `GET /api/datasets`,
`GET /api/series/{ticker}?interval=1d`, `GET /api/indicators`,
`POST /api/fetch {"ticker","period","interval"}`.

## Shipped indicator packs

| Indicator | Stance logic |
|---|---|
| SMA crossover 20/50 | fast above/below slow |
| EMA crossover 12/26 | fast above/below slow |
| RSI 14 | mean reversion at 30/70 |
| MACD 12/26/9 | MACD line vs signal line |
| Bollinger Bands 20 | mean reversion at the bands |
| ATR 14 | non-directional (volatility column only) |
| Stochastic 14/3/3 | mean reversion at 20/80 on %K |
| ADX 14 (+DI/−DI) | Neutral until ADX ≥ 25, then +DI vs −DI |
| Ichimoku 9/26/52 | price above/below/inside the cloud |
| Parabolic SAR | price vs trailing SAR |
| SuperTrend 10/3 | band direction |
| MFI 14 | volume-weighted mean reversion at 20/80 |
| CCI 20 | mean reversion outside ±100 |
| Donchian 20 | breakout of the prior bar's channel |
| OBV | volume flow vs its 20-bar average |
| Engulfing | pattern match on the bar (bullish/bearish) |
| Hammer / Shooting Star | pattern match on the bar |
| Marubozu | pattern match on the bar |

The first six are the Standard pack, the next nine the Advanced pack, and
the candlestick patterns the Patterns pack. All of them — plus any plugins
you add — flow through the same engine and trigger rule. Indicator math
validates its parameters (`ArgumentOutOfRangeException` on nonsense
lookbacks) rather than silently computing garbage.

## Dividends

Dividend events arrive automatically with every `fetch` (same Yahoo request,
no extra call) and are stored in `data/<TICKER>_dividends.csv`, merged on
re-fetch like candles. The app computes the dividend profile from stored
data — no network needed:

```bash
dotnet run --project src/EricoreTech.CandleSharp.Cli -- dividends KO
```

shows current yield (trailing 12-month payouts vs the latest close), the
per-year totals, dividend growth (YoY, 3-year and 5-year CAGR), and the
consecutive-growth-year streak. The dashboard shows the same in a Dividends
card (`GET /api/dividends/{ticker}` for scripting). Note that candle prices
are dividend-adjusted, so indicators and backtests already reflect total
return; this feature adds the explicit payout history and growth on top.

## Buy & hold simulator

Answer "what if I had put $X in a year ago?" from stored data:

```bash
dotnet run --project src/EricoreTech.CandleSharp.Cli -- simulate KO --amount 10000
dotnet run --project src/EricoreTech.CandleSharp.Cli -- simulate KO --amount 5000 --start 2024-01-02 --end 2024-12-31
```

You buy at the start-date close, hold, and collect each dividend as cash;
the report shows shares bought, final stock value, dividend cash (and how
many payments), total value with the overall return split into price vs
dividends, plus the dividends-reinvested figure for comparison. Because
stored prices are dividend-adjusted, the simulator first reconstructs the
raw price series from the dividend history so cash dividends are never
double counted. The dashboard has the same as the Buy & hold simulator
card (`GET /api/simulate/{ticker}?amount=&start=&end=`); the default
window is one year back from the latest bar.

## Social sentiment

See what the crowd is saying (source: StockTwits' public symbol stream — no
API key, authors tag their own posts Bullish/Bearish; note it is
unauthenticated and rate-limited, so pull occasionally):

```bash
dotnet run --project src/EricoreTech.CandleSharp.Cli -- social KO --fetch   # pull + show
dotnet run --project src/EricoreTech.CandleSharp.Cli -- social KO           # show stored
```

Posts are stored in `data/<TICKER>_social.json` (merged on re-pull) and
summarized into a bullish ratio: author-tagged posts count directly, and
untagged posts are scored with a small finance lexicon. The dashboard's
Social buzz card shows the summary and recent posts with a "Pull latest
chatter" button (`GET /api/social/{ticker}`, `POST
/api/social/{ticker}/refresh`).

A **Crowd Sentiment** agent feeds the stored chatter into the analyst panel
and the verdict — deliberately conservative: Neutral without 10+ posts,
Neutral when the newest post is over a week old, and directional only past
a 60/40 bullish-ratio threshold. Social data has no history, so the
backtester runs this agent without social input (it reports Neutral there)
rather than pretending past sentiment was knowable.

## The research report

One command assembles everything — verdict, all agents, the walk-forward
backtest, dividends, crowd sentiment, and the one-year simulation — into a
single document:

```bash
dotnet run --project src/EricoreTech.CandleSharp.Cli -- report KO          # deep dive on one ticker
dotnet run --project src/EricoreTech.CandleSharp.Cli -- report             # morning packet across all tickers
```

The dashboard's Report link opens the same as a printable HTML page
(`/report/{ticker}`). The packet mode gives one line per ticker — verdict
headline, yield, crowd read, and the best backtested agent — for a fast
morning scan; the deep dive is the full page.

## The bottom line

Everything above rolls up into one plain-English answer per ticker:

```bash
dotnet run --project src/EricoreTech.CandleSharp.Cli -- verdict AAPL
```

prints a headline (`LEANING BUY` / `LEANING SELL / AVOID` / `NO CLEAR
EDGE`) with a conviction percentage, followed by the price snapshot, the
agent tally, the two strongest agent voices, the market-regime context,
the risk manager's sizing suggestion, and the latest signals. The
dashboard shows the same thing in the Bottom line card at the top of each
dataset. The verdict is a confidence-weighted vote across the directional
agents, and it only leans when the margin clears a threshold — honest
disagreement reads as "no clear edge," not a coin flip.

## Trading agents

On top of the indicator engine sits an agent layer (design borrowed from
[ai-hedge-fund-net](https://github.com/riccardone/ai-hedge-fund-net)): an
agent implements `ITradingAgent`, reads prepared data (candles + the
engine's stances and triggers — agents never fetch), scores it into
itemized scorecards with deterministic rules, and emits one `TradeSignal`
(direction, 0-100 confidence, reasoning). Agents are plugins, loaded from
the same plugins directory as indicators.

```bash
dotnet run --project src/EricoreTech.CandleSharp.Cli -- analyze AAPL
dotnet run --project src/EricoreTech.CandleSharp.Cli -- analyze AAPL --agents consensus,trend_follower
```

The dashboard shows each agent's verdict, confidence, reasoning, and
expandable scorecards in the Analysts card. Shipped agents:

- **Trend Follower** — moving-average crosses, trailing stops (PSAR,
  SuperTrend), ADX strength, Ichimoku cloud
- **Mean Reverter** — oscillators (RSI, Stochastic, MFI, CCI) and Bollinger
  Bands; Bullish means "oversold, expect a bounce"
- **Consensus** — every loaded indicator votes with its current stance,
  plus a read on recent trigger direction
- **Scorekeeper** — measures how price actually moved after each
  indicator's past triggers on this ticker, then weights the vote by each
  indicator's real hit rate
- **Regime Detector** — classifies the market as trending, range-bound, or
  squeezing (ADX, ATR percentile, Bollinger band width); directional only
  in a real trend
- **Risk Manager** — non-directional position sizing: fixed account risk
  per trade with an ATR-multiple stop, capped at 20% of the account
- **ML Logistic** — dependency-free logistic regression over indicator
  features (RSI, MACD histogram, ADX, band position, ROC, ATR), trained on
  the ticker's own history; the predicted up-probability is the confidence
- **Hedge Meta** — online multiplicative-weights learner that replays past
  checkpoints to learn WHICH agents to trust, then casts a trust-weighted
  vote; adapts as regimes change

Two more ways to use the agents:

```bash
dotnet run --project src/EricoreTech.CandleSharp.Cli -- screen              # rank every saved ticker by consensus
dotnet run --project src/EricoreTech.CandleSharp.Cli -- watch --fetch       # refresh data, report verdict CHANGES only
```

`screen` also lives in the dashboard: when more than one dataset is saved, a
Screener card ranks them by consensus and clicking a row opens it. `watch`
remembers the last run's verdicts (in `data/.agent-state.<interval>.json`)
and prints only flips like `CHANGED AAPL Trend Follower Bullish -> Bearish`,
so scheduling it (cron / Task Scheduler) gives you a daily change report.

Judge the agents honestly with the walk-forward backtester — at every
checkpoint the engine and agent are re-run on truncated history, so nothing
can peek at the future:

```bash
dotnet run --project src/EricoreTech.CandleSharp.Cli -- backtest AAPL --horizon 10 --step 5
```

It prints each agent's directional call count, hit rate, and average /
cumulative direction-aligned forward return. Treat any agent (especially
the ML ones) as unproven until this table says otherwise.

To add your own agent, implement `ITradingAgent` in a class library
referencing Core and drop the DLL in `plugins/` — same as an indicator
plugin. The reasoning strings are generated from the scorecards in code;
the contract is shaped so an LLM narrator (as in ai-hedge-fund-net's
`LlmTradeSignalGenerator`) can be slotted in later without changing agents
or hosts.

## The indicator layers

1. **Math** (`Core/Indicators.cs`, `Core/IndicatorsAdvanced.cs`) — pure
   functions returning candle-aligned arrays (`null` during warm-up), shared
   by every indicator pack.
2. **Indicators** (the pack projects + plugins) — classes implementing the
   one shared contract, `ITechnicalIndicator`: output columns plus a per-bar
   stance (`Bullish` / `Bearish` / `Neutral`).
3. **Engine** (`Core/IndicatorEngine.cs`) — runs any set of indicators and
   derives trigger points with one uniform rule: a trigger fires on the bar
   where a stance changes to a directional value.

## Writing an indicator plugin

A plugin is a class library that references `EricoreTech.CandleSharp.Core` and
contains public `ITechnicalIndicator` classes — exactly how the shipped
indicator packs are built, so any of them doubles as a worked example.
A minimal custom indicator:

```csharp
public sealed class ObvIndicator(int smaWindow = 20) : ITechnicalIndicator
{
    public string Name => $"OBV_{smaWindow}";

    public IndicatorResult Compute(IReadOnlyList<Candle> candles)
    {
        double?[] obv = ...;     // your math
        double?[] obvSma = ...;
        return new IndicatorResult(
            [("OBV", obv), ($"OBV_SMA_{smaWindow}", obvSma)],
            Stances.FromComparison(obv, obvSma));
    }
}
```

Build it and drop the DLL into the `plugins/` folder next to where you run the
CLI or web app:

```bash
dotnet build MyIndicators -c Release
cp MyIndicators/bin/Release/net8.0/MyIndicators.dll plugins/
dotnet run --project src/EricoreTech.CandleSharp.Web
```

On startup the host scans the folder (plus a `plugins` folder next to the
executable, for published deployments) and instantiates every public
`ITechnicalIndicator` (parameterless or all-defaults constructor). On a
`Name` clash the later-loaded indicator wins, so your plugin can override a
shipped indicator by reusing its name. Plugin indicators show up everywhere
automatically: CSV columns, `signals` output, the dashboard's stance chips
and trigger table.

Constructors need every parameter to have a default (the loader instantiates
with defaults). `Stances.FromComparison`, `Stances.FromThresholds`, and
`Stances.None` cover the common stance shapes.

## Tests

```bash
dotnet run --project tests/EricoreTech.CandleSharp.Tests
```

Framework-free (no NuGet needed): covers CSV round-trips and merging,
indicator math, the engine's trigger rule, and plugin discovery/registry
behavior.
