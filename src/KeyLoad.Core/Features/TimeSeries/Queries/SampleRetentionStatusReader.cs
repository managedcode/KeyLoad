using KeyLoad.Storage;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleRetentionStatusReader
{
    internal static SampleRetentionStatus Read(DatabaseEngine database, IKeyValueView view, string principalId,
        ReadSampleRetentionRequest request, ReadExecutionBudget budget)
    {
        const int PurgedCountEmptyCount = 0;

        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);

        _ = SampleReadScope.Open(database, view, principalId, request.Partition, request.Set, request.SeriesId);
        var state = SampleRetentionStateReader.Read(view, request.Partition, request.Set, request.SeriesId, budget);
        budget.Check();
        return state is null
            ? new(null, PurgedCountEmptyCount, false)
            : new(SampleRetentionStateReader.Before(state), state.PurgedCount, state.HasMore);
    }
}
