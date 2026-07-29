namespace EricoreTech.CandleSharp.Domain
{
    public sealed record BacktestOptions(int Warmup = 60, int Horizon = 10, int Step = 5);
}
