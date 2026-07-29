using System.Globalization;
using System.Net;
using System.Text;
using EricoreTech.CandleSharp.Application;
using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Web
{
    /// <summary>Renders a TickerReport as a self-contained, print-friendly HTML page.</summary>
    internal static class ReportHtml
    {
        private const string Head = """
            <!DOCTYPE html>
            <html lang="en"><head><meta charset="utf-8">
            <style>
              body { font: 14px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; color: #111; margin: 40px auto; max-width: 780px; padding: 0 20px; }
              h1 { font-size: 20px; border-bottom: 2px solid #111; padding-bottom: 6px; }
              h2 { font-size: 14px; text-transform: uppercase; letter-spacing: 0.06em; margin: 22px 0 6px; color: #444; }
              .headline { font-size: 18px; font-weight: 700; margin: 10px 0; }
              .bull { color: #006300; } .bear { color: #b32222; } .flat { color: #666; }
              table { border-collapse: collapse; width: 100%; font-size: 13px; }
              th, td { text-align: left; padding: 3px 10px 3px 0; border-bottom: 1px solid #ddd; }
              th { color: #666; font-weight: 500; }
              td.num { font-variant-numeric: tabular-nums; }
              ul { margin: 4px 0; padding-left: 20px; color: #333; }
              .muted { color: #777; font-size: 12px; }
              .post { border-left: 3px solid #ddd; padding-left: 8px; margin: 4px 0; font-size: 13px; color: #333; }
              @media print { body { margin: 0 auto; } }
            </style></head><body>
            """;

        public static string Render(TickerReport r)
        {
            var sb = new StringBuilder();
            sb.Append(Head.Replace("<style>", $"<title>{Enc(r.Ticker)} — CandleSharp report</title>\n<style>"));
            sb.Append($"<h1>CandleSharp Research Report — {Enc(r.Ticker)} ({Enc(r.Interval)})</h1>");
            sb.Append($"<p class=\"headline {DirClass(r.Verdict.Direction)}\">{Icon(r.Verdict.Direction)} {Enc(r.Verdict.Headline)}</p>");

            sb.Append("<ul>");
            foreach (var detail in r.Verdict.Details)
                sb.Append($"<li>{Enc(detail)}</li>");
            sb.Append("</ul>");

            sb.Append("<h2>Analysts</h2><table><tr><th>Agent</th><th>Verdict</th><th>Reasoning</th></tr>");
            foreach (var agent in r.Agents.OrderByDescending(a => a.Signal.Confidence))
            {
                sb.Append($"<tr><td>{Enc(agent.DisplayName)}</td>");
                sb.Append(Inv($"<td class=\"{DirClass(agent.Signal.Direction)}\">{Icon(agent.Signal.Direction)} {agent.Signal.Direction} {agent.Signal.Confidence:0}%</td>"));
                sb.Append($"<td>{Enc(agent.Signal.Reasoning)}</td></tr>");
            }
            sb.Append("</table>");

            sb.Append("<h2>Backtest — how often each agent was right here</h2>");
            sb.Append("<table><tr><th>Agent</th><th>Calls</th><th>Hit rate</th><th>Cumulative move</th></tr>");
            foreach (var b in r.Backtests)
            {
                string hit = b.Directional == 0 ? "-" : b.HitRate.ToString("P0", CultureInfo.InvariantCulture);
                string cum = b.Directional == 0 ? "-" : b.CumulativeAlignedReturn.ToString("+0.0%;-0.0%", CultureInfo.InvariantCulture);
                sb.Append($"<tr><td>{Enc(b.AgentKey)}</td><td class=\"num\">{b.Directional}</td>");
                sb.Append($"<td class=\"num\">{hit}</td><td class=\"num\">{cum}</td></tr>");
            }
            sb.Append("</table>");

            sb.Append("<h2>Dividends</h2>");
            if (r.Dividends.PaymentCount == 0)
                sb.Append("<p class=\"muted\">No dividend history stored.</p>");
            else
            {
                sb.Append(Inv($"<p>Trailing year {r.Dividends.TrailingYearTotal:0.####}/share"));
                if (r.Dividends.YieldPercent is { } y) sb.Append(Inv($" — yield {y:0.0}%"));
                sb.Append(Inv($" — {r.Dividends.ConsecutiveGrowthYears} consecutive growth year(s)"));
                if (r.Dividends.GrowthYoYPercent is { } g1) sb.Append(Inv($" — growth {g1:+0.0;-0.0}% YoY"));
                if (r.Dividends.Growth5YPercent is { } g5) sb.Append(Inv($", {g5:+0.0;-0.0}%/yr over 5y"));
                sb.Append("</p>");
            }

            sb.Append("<h2>Crowd sentiment</h2>");
            if (r.Social.PostCount == 0)
                sb.Append("<p class=\"muted\">No chatter stored.</p>");
            else
            {
                sb.Append(Inv($"<p>{r.Social.PostCount} posts"));
                if (r.Social.BullishRatioPercent is { } ratio) sb.Append(Inv($", {ratio:0}% bullish"));
                sb.Append(Inv($" (tagged {r.Social.TaggedBullish}▲/{r.Social.TaggedBearish}▼, newest {r.Social.NewestPost:yyyy-MM-dd})</p>"));
                foreach (var post in r.Social.RecentPosts.Take(4))
                    sb.Append($"<div class=\"post\"><b>@{Enc(post.Author)}</b> {Enc(post.Text)}</div>");
            }

            sb.Append("<h2>One-year buy &amp; hold</h2>");
            if (r.Simulation is not { } sim)
                sb.Append("<p class=\"muted\">Not enough daily history for a one-year simulation.</p>");
            else
            {
                string cls = sim.TotalReturnPercent >= 0 ? "bull" : "bear";
                sb.Append(Inv($"<p>{sim.Invested:0} invested on {sim.BuyDate:yyyy-MM-dd} became "));
                sb.Append(Inv($"<b class=\"{cls}\">{sim.EndTotalValue:0.00} ({sim.TotalReturnPercent:+0.0;-0.0}%)</b>"));
                sb.Append(Inv($" — price {sim.PriceReturnPercent:+0.0;-0.0}% plus {sim.DividendCash:0.00} dividend cash over {sim.DividendPayments} payments"));
                sb.Append(Inv($" ({sim.DividendYieldOnCostPercent:+0.0;-0.0}% on cost). Reinvested: {sim.ReinvestedValue:0.00} ({sim.ReinvestedReturnPercent:+0.0;-0.0}%).</p>"));
            }

            sb.Append("<p class=\"muted\">Automated technical read of stored data only — not financial advice.</p>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static string Enc(string? text) => WebUtility.HtmlEncode(text ?? "");

        private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

        private static string DirClass(SignalDirection d) => d switch
        {
            SignalDirection.Bullish => "bull",
            SignalDirection.Bearish => "bear",
            _ => "flat",
        };

        private static string Icon(SignalDirection d) => d switch
        {
            SignalDirection.Bullish => "▲",
            SignalDirection.Bearish => "▼",
            _ => "▬",
        };
    }
}
