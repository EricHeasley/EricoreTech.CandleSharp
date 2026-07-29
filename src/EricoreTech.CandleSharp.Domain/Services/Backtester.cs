namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// Walk-forward replay: truncates history at each checkpoint, reruns the
    /// indicator engine on the truncated window, and hands the agent only what
    /// it could have known at the time — no lookahead by construction.
    /// </summary>
    public static class Backtester
    {
        public static BacktestReport Run(
            ITradingAgent agent,
            IndicatorEngine engine,
            string ticker,
            string interval,
            IReadOnlyList<Candle> candles,
            BacktestOptions? options = null)
        {
            var o = options ?? new BacktestOptions();
            ArgumentOutOfRangeException.ThrowIfLessThan(o.Warmup, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(o.Horizon, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(o.Step, 1);

            int checkpoints = 0, directional = 0, aligned = 0;
            double alignedReturnSum = 0;

            for (int t = o.Warmup; t + o.Horizon < candles.Count; t += o.Step)
            {
                var window = candles.Take(t + 1).ToList();
                var signals = engine.Run(window);
                var report = agent.Analyze(new AgentInput(ticker, interval, window, signals));
                checkpoints++;

                if (report.Signal.Direction == SignalDirection.Neutral) continue;
                directional++;
                double forward = (candles[t + o.Horizon].Close - candles[t].Close) / candles[t].Close;
                double alignedReturn = report.Signal.Direction == SignalDirection.Bullish ? forward : -forward;
                if (alignedReturn > 0) aligned++;
                alignedReturnSum += alignedReturn;
            }

            return new BacktestReport(
                agent.Key, agent.DisplayName, ticker.ToUpperInvariant(), interval,
                checkpoints, directional, aligned,
                directional == 0 ? 0 : (double)aligned / directional,
                directional == 0 ? 0 : alignedReturnSum / directional,
                alignedReturnSum);
        }
    }
}
