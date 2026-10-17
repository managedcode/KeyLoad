using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal readonly record struct TimescaleTimeSeriesIntensiveAppendRow(int Ordinal, Guid CommandId, long Sequence);

internal static class TimescaleTimeSeriesIntensiveReceipt
{
    private const int LastElementOffset = 1;

    internal static TimeSeriesIntensiveAppendReceipt ValidateScalar(TimeSeriesIntensiveAppendReceipt? actual,
        Guid expectedCommandId)
    {
        if (actual is null || actual.CommandId == Guid.Empty || actual.CommandId != expectedCommandId
            || actual.Sequence < TimescaleTimeSeriesIntensiveProtocol.MinimumSequence)
        {
            throw Invalid();
        }
        return actual;
    }

    internal static TimeSeriesIntensiveAppendReceipt ValidateBatch(ImmutableArray<TimescaleTimeSeriesIntensiveAppendRow> actual,
        Guid expectedCommandId, int batchOrdinal)
    {
        const int NoObservedItems = 0;
        const int FirstElementIndex = 0;
        const int AdjacentElementOffset = 1;

        if (actual.IsDefault || actual.Length != TimeSeriesIntensiveProfile.SeedBatchSize
            || expectedCommandId == Guid.Empty || batchOrdinal < NoObservedItems
            || batchOrdinal >= TimeSeriesIntensiveProfile.SeedBatchCount)
        {
            throw Invalid();
        }

        var sequence = batchOrdinal * (long)TimeSeriesIntensiveProfile.SeedBatchSize;
        for (var index = FirstElementIndex; index < actual.Length; index++)
        {
            var row = actual[index];
            if (row.Ordinal != index + AdjacentElementOffset || row.CommandId != expectedCommandId
                || row.Sequence != sequence + index + TimescaleTimeSeriesIntensiveProtocol.SeedSequenceOffset)
            {
                throw Invalid();
            }
        }
        var terminal = actual[^LastElementOffset];
        return new(terminal.CommandId, terminal.Sequence);
    }

    private static ComparisonFailureException Invalid() =>
        new(TimescaleTimeSeriesIntensiveProtocol.InvalidReceipt);
}
