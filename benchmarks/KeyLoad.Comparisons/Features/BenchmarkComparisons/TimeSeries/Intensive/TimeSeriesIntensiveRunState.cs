using System.Collections.Immutable;
using System.Diagnostics;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveRunState(TimeSeriesIntensiveScenario scenario)
{
    internal TimeSeriesIntensiveAttempt[] Storage { get; } = TimeSeriesIntensiveAttemptLedger.CreateMeasuredStorage();
    internal ImmutableArray<TimeSeriesIntensiveRepetitionResult>.Builder Repetitions { get; } =
        ImmutableArray.CreateBuilder<TimeSeriesIntensiveRepetitionResult>(TimeSeriesIntensiveProfile.RepetitionCount);
    internal TimeSeriesIntensiveRunStage Stage { get; set; } = TimeSeriesIntensiveRunStage.Preparation;
    internal bool SeedVerified { get; set; }
    internal int Repetition { get; set; }
    internal TimeSeriesIntensiveRepetitionExecutor? Executor { get; set; }

    internal TimeSeriesIntensiveRunResult Finish(Exception? error = null)
    {
        var captured = error is null ? default : TimeSeriesIntensiveFailure.Capture(error);
        TimeSeriesIntensiveRunFailure? failure = error is null ? null : new(Executor?.Stage ?? Stage,
            Math.Min(Repetition, TimeSeriesIntensiveProfile.RepetitionCount - 1),
            captured.Outcome, captured.Failure);
        return new(scenario, Stopwatch.Frequency, Storage, Repetitions.ToImmutable(), SeedVerified, failure);
    }
}
