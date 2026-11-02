using KeyLoad.Storage;

namespace KeyLoad.Core.TimeSeries;

internal static class SampleRetentionStatusReader
{
    internal static SampleRetentionStatus Read(DatabaseEngine database, IKeyValueView view, string principalId,
        ReadSampleRetentionRequest request, ReadExecutionBudget budget)
    {
        const int PurgedCountEmptyCount = 0;

        ArgumentNullException.ThrowIfNull(database) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(view) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;

        _ = SampleReadScope.Open(database, view, principalId, request.Partition, request.Set, request.SeriesId);
        var state = (SampleRetentionStateReader.Read(view, request.Partition, request.Set, request.SeriesId, budget)).Value;
        budget.Check();
        return state is null
            ? new(null, PurgedCountEmptyCount, false)
            : new(SampleRetentionStateReader.Before(state), state.PurgedCount, state.HasMore);
    }
}
