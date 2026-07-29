using System.Globalization;
using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Agents
{
    /// <summary>
    /// Classifies the market regime — trending, range-bound, or squeezing — from
    /// ADX, ATR percentile, and Bollinger band width. Directional only when a
    /// real trend is in force; otherwise its value is the context in the
    /// reasoning (trust trend-followers in trends, mean-reverters in ranges).
    /// </summary>
    public sealed class RegimeAgent(double trendAdx = 25, double rangeAdx = 20, double squeezePercentile = 20)
        : ITradingAgent
    {
        public string Key => "regime";
        public string DisplayName => "Regime Detector";

        public AgentReport Analyze(AgentInput input)
        {
            var scores = new List<ScoreCard>();

            var adxColumn = SeriesLookup.Column(input, "ADX_");
            double? adx = SeriesLookup.LastValue(adxColumn);
            string regime = adx is null ? "unknown (ADX not loaded)"
                : adx >= trendAdx ? "trending"
                : adx <= rangeAdx ? "range-bound"
                : "transitional";
            scores.Add(new ScoreCard("Trend strength", Math.Round(adx ?? 0), 100,
                [adx is null ? "ADX indicator not loaded" : $"ADX {adx:0.0} -> {regime} (trend above {trendAdx}, range below {rangeAdx})"]));

            var atrColumn = SeriesLookup.Column(input, "ATR_");
            double? atrPct = null;
            if (atrColumn is not null && SeriesLookup.LastValue(atrColumn) is { } atrNow)
            {
                atrPct = SeriesLookup.Percentile(atrColumn.Where(v => v is not null).Select(v => v!.Value), atrNow);
                scores.Add(new ScoreCard("Volatility", Math.Round(atrPct.Value), 100,
                    [$"ATR is at the {atrPct:0}th percentile of the loaded history"]));
            }

            var upper = SeriesLookup.Column(input, "BB_upper");
            var lower = SeriesLookup.Column(input, "BB_lower");
            var mid = SeriesLookup.Column(input, "BB_mid");
            bool squeeze = false;
            if (upper is not null && lower is not null && mid is not null)
            {
                var widths = new List<double>();
                for (int i = 0; i < upper.Length; i++)
                    if (upper[i] is { } u && lower[i] is { } l && mid[i] is { } m && m != 0)
                        widths.Add((u - l) / m);
                if (widths.Count > 0)
                {
                    double widthPct = SeriesLookup.Percentile(widths, widths[^1]);
                    squeeze = widthPct <= squeezePercentile;
                    scores.Add(new ScoreCard("Bollinger squeeze", Math.Round(widthPct), 100,
                        [$"Band width is at the {widthPct:0}th percentile" + (squeeze ? " — squeeze on, expect expansion" : "")]));
                }
            }

            var direction = SignalDirection.Neutral;
            double confidence = 0;
            if (regime == "trending" && input.Signals.Stances.Keys.FirstOrDefault(k => k.StartsWith("ADX_")) is { } adxKey)
            {
                (direction, _) = Stances.LatestRun(input.Signals.Stances[adxKey]);
                confidence = direction == SignalDirection.Neutral ? 0 : Math.Round(Math.Min(100, adx ?? 0));
            }

            string reasoning = $"Regime: {regime}."
                + (atrPct is { } p ? $" Volatility at the {p:0}th percentile." : "")
                + (squeeze ? " Bollinger squeeze in effect." : "")
                + (regime == "trending"
                    ? " Trend-following signals carry more weight here."
                    : regime == "range-bound" ? " Mean-reversion signals carry more weight here." : "");
            return new AgentReport(Key, DisplayName, input.Ticker,
                new TradeSignal(direction, confidence, reasoning), scores);
        }
    }
}
