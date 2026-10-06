using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveRunState(TimeSeriesIntensiveScenario scenario, TimeProvider? provider = null)
{
    private readonly TimeProvider timeProvider = provider ?? TimeProvider.System;
    internal TimeSeriesIntensiveAttempt[] Storage { get; } = TimeSeriesIntensiveAttemptLedger.CreateMeasuredStorage();
    internal TimeSeriesIntensiveAttempt[] WarmupStorage { get; } = TimeSeriesIntensiveAttemptLedger.CreateWarmupStorage();
    internal ImmutableArray<TimeSeriesIntensiveRepetitionResult>.Builder Repetitions { get; } =
        ImmutableArray.CreateBuilder<TimeSeriesIntensiveRepetitionResult>(TimeSeriesIntensiveProfile.RepetitionCount);
    internal TimeSeriesIntensiveRunStage Stage { get; set; } = TimeSeriesIntensiveRunStage.Preparation;
    internal bool SeedVerified { get; set; }
    internal int Repetition { get; set; }
    internal TimeSeriesIntensiveRepetitionExecutor? Executor { get; set; }

    internal TimeSeriesIntensiveRunResult Finish(Exception? error = null)
    {
        const int SingleItemCount = 1;

        var captured = error is null ? default : TimeSeriesIntensiveFailure.Capture(error);
        TimeSeriesIntensiveRunFailure? failure = error is null ? null : new(Executor?.Stage ?? Stage,
            Math.Min(Repetition, TimeSeriesIntensiveProfile.RepetitionCount - SingleItemCount),
            captured.Outcome, captured.Failure);
        return new(scenario, timeProvider.TimestampFrequency, Storage, Repetitions.ToImmutable(), SeedVerified, failure)
        {
            WarmupAttempts = WarmupStorage
        };
    }
}
