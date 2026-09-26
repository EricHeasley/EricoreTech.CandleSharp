namespace EricoreTech.CandleSharp.Blazor.Services
{
    /// <summary>
    /// The one piece of UI state genuinely shared across components in a
    /// browser tab: which ticker/interval is selected. The watchlist sidebar
    /// sets it, the ticker header and main content read it. Everything more
    /// local to one part of the screen — the chart's zoom/pan/overlay picks,
    /// which tab is active — lives as component-local state instead, so
    /// panning the chart doesn't ripple into "the data changed, reload
    /// everything" the way a ticker switch or a fetch does.
    /// </summary>
    public sealed class DashboardState
    {
        public string? Ticker { get; private set; }
        public string Interval { get; private set; } = "1d";

        public event Action? Changed;

        public void Select(string ticker, string interval = "1d")
        {
            // Always notify, even for the same ticker: a re-fetch just extended its
            // history, and relies on this to trigger the reload.
            Ticker = ticker;
            Interval = interval;
            Changed?.Invoke();
        }

        /// <summary>Call after a fetch/refresh so listeners reload without changing the selection.</summary>
        public void NotifyDataChanged() => Changed?.Invoke();
    }
}
