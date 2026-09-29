namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>Settings for <see cref="MLGuidanceAnalyzer"/>.</summary>
    public sealed record MLGuidanceOptions(
        int Horizon = 10,
        int Warmup = 250,
        int Step = 25,
        int NumTrees = 20,
        int MaxDepth = 3,
        double LearningRate = 0.15,
        double NeutralBand = 0.05)
    {
        public void Validate()
        {
            if (Horizon < 1) throw new ArgumentOutOfRangeException(nameof(Horizon), "horizon must be at least 1 bar");
            if (Warmup < 30) throw new ArgumentOutOfRangeException(nameof(Warmup), "warmup must be at least 30 bars");
            if (Step < 1) throw new ArgumentOutOfRangeException(nameof(Step), "step must be at least 1 bar");
            if (NumTrees < 1) throw new ArgumentOutOfRangeException(nameof(NumTrees), "need at least 1 tree");
            if (MaxDepth < 1) throw new ArgumentOutOfRangeException(nameof(MaxDepth), "max depth must be at least 1");
            if (LearningRate <= 0) throw new ArgumentOutOfRangeException(nameof(LearningRate), "learning rate must be positive");
            if (NeutralBand is < 0 or >= 0.5) throw new ArgumentOutOfRangeException(nameof(NeutralBand), "neutral band must be in [0, 0.5)");
        }
    }

    /// <summary>One feature's share of the model's total decision-making, 0..1.</summary>
    public sealed record FeatureScore(string Name, double Importance);

    /// <summary>
    /// One slice of the reliability diagram: among walk-forward predictions
    /// whose probability fell in this range, how often the stock actually
    /// went up. A well-calibrated model's ActualUpRate tracks its own
    /// predicted probabilities; a poorly calibrated one's "80% confident"
    /// might really only be right 55% of the time.
    /// </summary>
    public sealed record CalibrationBucket(string Range, int Samples, double PredictedAvg, double ActualUpRate);

    /// <summary>
    /// Output of training a <see cref="GradientBoostedTrees"/> model on one
    /// ticker's own history to predict whether it will be up Horizon bars from
    /// now. Direction/Confidence/Probability come from a model trained on
    /// every bar with a fully known outcome; everything else (Checkpoints
    /// through Calibration) is the walk-forward honesty check — at each
    /// checkpoint a fresh model is trained on only the data available by then
    /// and scored against what actually happened, exactly like
    /// <see cref="Backtester"/> does for the rule-based agents.
    /// </summary>
    public sealed record MLGuidanceReport(
        string Ticker,
        MLGuidanceOptions Options,
        int EffectiveWarmup,
        IReadOnlyList<string> Excluded,
        int FeatureCount,
        DateTime AsOf,
        SignalDirection Direction,
        double Confidence,
        double Probability,
        IReadOnlyList<FeatureScore> TopFeatures,
        int Checkpoints,
        int Directional,
        int Aligned,
        double HitRate,
        double AvgAlignedReturn,
        double CumulativeAlignedReturn,
        double BaselineHitRate,
        IReadOnlyList<CalibrationBucket> Calibration);
}
