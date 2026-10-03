using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal readonly record struct TimescaleTimeSeriesIntensiveAppendRow(int Ordinal, Guid CommandId, long Sequence);

internal static class TimescaleTimeSeriesIntensiveReceipt
{
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
        if (actual.IsDefault || actual.Length != TimeSeriesIntensiveProfile.SeedBatchSize
            || expectedCommandId == Guid.Empty || batchOrdinal < 0
            || batchOrdinal >= TimeSeriesIntensiveProfile.SeedBatchCount)
        {
            throw Invalid();
        }

        var sequence = batchOrdinal * (long)TimeSeriesIntensiveProfile.SeedBatchSize;
        for (var index = 0; index < actual.Length; index++)
        {
            var row = actual[index];
            if (row.Ordinal != index + 1 || row.CommandId != expectedCommandId
                || row.Sequence != sequence + index + TimescaleTimeSeriesIntensiveProtocol.SeedSequenceOffset)
            {
                throw Invalid();
            }
        }
        var terminal = actual[^1];
        return new(terminal.CommandId, terminal.Sequence);
    }

    private static ComparisonFailureException Invalid() =>
        new(TimescaleTimeSeriesIntensiveProtocol.InvalidReceipt);
}
