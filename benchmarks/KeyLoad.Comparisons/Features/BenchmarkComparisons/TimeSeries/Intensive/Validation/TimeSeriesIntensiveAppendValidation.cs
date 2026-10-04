using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveAppendValidation
{
    private readonly ImmutableArray<Guid> commandIds;
    private readonly int[] sequences;

    internal TimeSeriesIntensiveAppendValidation(ImmutableArray<Guid> commandIds)
    {
        if (commandIds.IsDefaultOrEmpty || commandIds.Length > TimeSeriesIntensiveProfile.OperationCount)
        {
            throw new ArgumentException(TimeSeriesIntensiveRuntimeErrors.InvalidReceipt, nameof(commandIds));
        }

        this.commandIds = commandIds;
        sequences = new int[commandIds.Length];
    }

    internal void Validate(int index, TimeSeriesIntensiveAppendReceipt receipt)
    {
        if (commandIds.IsDefault || index < 0 || index >= commandIds.Length || receipt is null
            || receipt.CommandId != commandIds[index] || receipt.Sequence < 1 || receipt.Sequence > commandIds.Length)
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveRuntimeErrors.InvalidReceipt);
        }

        if (Interlocked.CompareExchange(ref sequences[(int)receipt.Sequence - 1], 1, 0) != 0)
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveRuntimeErrors.DuplicateSequence);
        }
    }

    internal void ValidateComplete()
    {
        if (commandIds.IsDefaultOrEmpty || sequences.Any(sequence => sequence != 1))
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveRuntimeErrors.MissingSequence);
        }
    }
}
