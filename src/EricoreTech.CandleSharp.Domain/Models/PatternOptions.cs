namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// Settings for <see cref="PatternAnalyzer"/>. Horizon is the number
    /// of bars ahead the forward return is measured over; the last TestFraction of
    /// each ticker's history is held out to check whether in-sample patterns hold.
    /// </summary>
    public sealed record PatternOptions(
        int Horizon = 10,
        int MinSamples = 30,
        double TestFraction = 0.3,
        int Buckets = 5,
        int Top = 10,
        bool IncludeCombos = true)
    {
        public void Validate()
        {
            if (Horizon < 1) throw new ArgumentOutOfRangeException(nameof(Horizon), "horizon must be at least 1 bar");
            if (MinSamples < 2) throw new ArgumentOutOfRangeException(nameof(MinSamples), "min samples must be at least 2");
            if (TestFraction is < 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(TestFraction), "test fraction must be in [0, 1)");
            if (Buckets < 2) throw new ArgumentOutOfRangeException(nameof(Buckets), "need at least 2 buckets");
            if (Top < 1) throw new ArgumentOutOfRangeException(nameof(Top), "top must be at least 1");
        }
    }
}
