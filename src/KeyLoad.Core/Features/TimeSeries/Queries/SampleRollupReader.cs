using KeyLoad.Storage;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleRollupReader
{
    private const long AbsentRevision = 0;
    internal static SampleRollupResult Read(DatabaseEngine database, IKeyValueView view, string principal,
        ReadSampleRollupRequest request, ReadExecutionBudget budget)
    {
        SampleRollupValidation.Range(request.From, request.UntilExclusive);
        _ = SampleReadScope.Open(database, view, principal, request.Partition, request.Set, request.SeriesId);
        var state = SampleRollupRecords.Read(view, request.Partition, request.Set, request.SeriesId,
            request.From, request.UntilExclusive);
        budget.Check();
        if (state is null || state.Dropped)
        { return new(state?.Revision ?? AbsentRevision, null); }
        var watermark = SampleRollupRecords.Watermark(view, request.Partition, request.Set, request.SeriesId);
        if (state.SourceSequence != watermark.Sequence || state.RetentionBeforeUtcTicks != watermark.Floor
            || watermark.Floor is { } floor && state.FromUtcTicks < floor)
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, SampleRollupProtocol.Stale); }
        var aggregate = SampleAggregateAccumulator.Reconstruct(state.Count, state.Sum, state.Minimum, state.Maximum);
        budget.Check();
        return new(state.Revision, new(new(state.FromUtcTicks, TimeSpan.Zero), new(state.UntilUtcTicks, TimeSpan.Zero),
            state.SourceSequence, state.RetentionBeforeUtcTicks is { } before ? new(before, TimeSpan.Zero) : null, aggregate));
    }
}
