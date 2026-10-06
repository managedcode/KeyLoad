using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record TimeSeriesIntensivePreparedPhase(TimeSeriesIntensiveScenario Scenario, int Repetition,
    bool Warmup, string SeriesId, ImmutableArray<Guid> Commands, TimeSeriesIntensiveAppendValidation? Appends)
{
    internal int Count => Warmup ? TimeSeriesIntensiveProfile.WarmupCount : TimeSeriesIntensiveProfile.OperationCount;

    internal static TimeSeriesIntensivePreparedPhase Create(TimeSeriesIntensiveScenario scenario, string runId, int repetition, bool warmup)
    {
        const int FirstElementIndex = 0;

        ArgumentOutOfRangeException.ThrowIfNegative(repetition);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(repetition, TimeSeriesIntensiveProfile.RepetitionCount);
        if (scenario != TimeSeriesIntensiveScenario.Append)
        {
            return new(scenario, repetition, warmup, TimeSeriesIntensiveProfile.SeedSeries, [], null);
        }

        var count = warmup ? TimeSeriesIntensiveProfile.WarmupCount : TimeSeriesIntensiveProfile.OperationCount;
        var commands = ImmutableArray.CreateBuilder<Guid>(count);
        for (var index = FirstElementIndex; index < count; index++)
        {
            commands.Add(TimeSeriesIntensivePlans.CommandId(runId, TimeSeriesIntensivePlans.CommandPurpose(repetition, warmup, index)));
        }

        var prepared = commands.MoveToImmutable();
        return new(scenario, repetition, warmup, TimeSeriesIntensivePlans.PhaseSeries(repetition, warmup), prepared, new(prepared));
    }
}
