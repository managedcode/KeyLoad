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
        const int NoItems = 0;
        const int AdjacentElementOffset = 1;
        const int SingleItemCount = 1;
        const int NoObservedItems = 0;

        if (commandIds.IsDefault || index < NoItems || index >= commandIds.Length || receipt is null
            || receipt.CommandId != commandIds[index] || receipt.Sequence < AdjacentElementOffset || receipt.Sequence > commandIds.Length)
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveRuntimeErrors.InvalidReceipt);
        }

        if (Interlocked.CompareExchange(ref sequences[(int)receipt.Sequence - SingleItemCount], SingleItemCount, NoObservedItems) != NoObservedItems)
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveRuntimeErrors.DuplicateSequence);
        }
    }

    internal void ValidateComplete()
    {
        const int SingleItemCount = 1;

        if (commandIds.IsDefaultOrEmpty || sequences.Any(sequence => sequence != SingleItemCount))
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveRuntimeErrors.MissingSequence);
        }
    }
}
