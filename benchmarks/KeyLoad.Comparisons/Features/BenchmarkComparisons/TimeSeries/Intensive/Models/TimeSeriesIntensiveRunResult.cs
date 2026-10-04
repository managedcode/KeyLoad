using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal enum TimeSeriesIntensiveRunStage
{
    Preparation,
    Initialization,
    Seed,
    SeedVerification,
    Warmup,
    WarmupVerification,
    Measured,
    FinalVerification
}

internal readonly record struct TimeSeriesIntensiveRunFailure(TimeSeriesIntensiveRunStage Stage,
    int Repetition, TimeSeriesIntensiveOutcome Outcome, TimeSeriesIntensiveFailure Failure);

internal sealed record TimeSeriesIntensiveRepetitionResult(int Repetition, TimeSeriesIntensivePhaseResult Warmup,
    TimeSeriesIntensivePhaseResult Measured, bool FinalVerified, TimeSeriesIntensiveRunFailure? Failure)
{
    internal bool Succeeded => Warmup.Succeeded && Measured.Succeeded && FinalVerified && Failure is null;

    internal TimeSeriesIntensiveRepetitionResult WithVerificationFailure(TimeSeriesIntensiveRunFailure failure) =>
        this with { FinalVerified = false, Failure = Failure ?? failure };
}

internal sealed record TimeSeriesIntensiveRunResult(TimeSeriesIntensiveScenario Scenario, long TimestampFrequency,
    ReadOnlyMemory<TimeSeriesIntensiveAttempt> Attempts, ImmutableArray<TimeSeriesIntensiveRepetitionResult> Repetitions,
    bool SeedVerified, TimeSeriesIntensiveRunFailure? Failure)
{
    internal ReadOnlyMemory<TimeSeriesIntensiveAttempt> WarmupAttempts { get; init; }

    internal bool Succeeded => SeedVerified && Failure is null && Repetitions.Length == TimeSeriesIntensiveProfile.RepetitionCount
        && Repetitions.All(repetition => repetition.Succeeded);
}
