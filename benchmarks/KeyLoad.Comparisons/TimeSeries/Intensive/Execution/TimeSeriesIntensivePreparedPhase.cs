using System.Collections.Immutable;

namespace KeyLoad.Comparisons.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record TimeSeriesIntensivePreparedPhase(TimeSeriesIntensiveScenario Scenario, int Repetition,
    string SeriesId, ImmutableArray<Guid> Commands, TimeSeriesIntensiveAppendValidation? Appends)
{
    internal static int Count => TimeSeriesIntensiveProfile.OperationCount;

    internal static TimeSeriesIntensivePreparedPhase Create(TimeSeriesIntensiveScenario scenario, string runId, int repetition)
    {
        const int FirstElementIndex = 0;

        ArgumentOutOfRangeException.ThrowIfNegative(repetition);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(repetition, TimeSeriesIntensiveProfile.RepetitionCount);
        if (scenario != TimeSeriesIntensiveScenario.Append)
        {
            return new(scenario, repetition, TimeSeriesIntensiveProfile.SeedSeries, [], null);
        }

        var count = TimeSeriesIntensiveProfile.OperationCount;
        var commands = ImmutableArray.CreateBuilder<Guid>(count);
        for (var index = FirstElementIndex; index < count; index++)
        {
            commands.Add(TimeSeriesIntensivePlans.CommandId(runId, TimeSeriesIntensivePlans.CommandPurpose(repetition, index)));
        }

        var prepared = commands.MoveToImmutable();
        return new(scenario, repetition, TimeSeriesIntensivePlans.PhaseSeries(repetition), prepared, new(prepared));
    }
}
