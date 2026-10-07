using KeyLoad.Storage;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleRollupCommands
{
    private const long AbsentRevision = 0;
    private const long NextRevision = 1;

    internal static MutationReceipt Refresh(DatabaseEngine database, IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef partition, RefreshSampleRollup request, int bucketLimit)
    {
        Scope(database, tx, principal, partition, request.SeriesSet, request.SeriesId,
            Capability.SeriesManage | Capability.SeriesRead);
        SampleRollupValidation.Range(request.From, request.UntilExclusive, request.ExpectedRevision);
        SampleAggregateReader.ValidateSampleLimit(request.MaxSamples, database.Limits.MaxScanRecords);
        var charge = new SampleRollupReadCharge(database.Limits.MaxQueryReadBytes);
        var previous = SampleRollupRecords.Read(tx, partition, request.SeriesSet, request.SeriesId,
            request.From, request.UntilExclusive, charge.Charge);
        Revision(previous, request.ExpectedRevision);
        if (previous is null)
        { AdmitBucket(tx, partition, request, bucketLimit, charge); }
        var watermark = SampleRollupRecords.Watermark(tx, partition, request.SeriesSet, request.SeriesId, charge.Charge);
        if (watermark.Floor is { } floor && request.From.UtcTicks < floor)
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, SampleRollupProtocol.Stale); }
        var aggregate = SampleRollupFold.Read(tx, partition, request, watermark.Sequence, charge);
        var next = new SampleRollupState(SampleRollupNative.Version, request.From.UtcTicks, request.UntilExclusive.UtcTicks,
            checked(request.ExpectedRevision + NextRevision), watermark.Sequence, watermark.Floor, false,
            aggregate.Count, aggregate.Sum, aggregate.Minimum, aggregate.Maximum);
        tx.PutRecord(SampleRollupKeys.Bucket(partition, request.SeriesSet, request.SeriesId,
            request.From, request.UntilExclusive), next);
        return new(SampleRollupProtocol.RefreshKind, request.SeriesSet, request.SeriesId, next.Revision);
    }

    internal static MutationReceipt Drop(DatabaseEngine database, IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef partition, DropSampleRollup request)
    {
        Scope(database, tx, principal, partition, request.SeriesSet, request.SeriesId, Capability.SeriesManage);
        SampleRollupValidation.Range(request.From, request.UntilExclusive, request.ExpectedRevision);
        var charge = new SampleRollupReadCharge(database.Limits.MaxQueryReadBytes);
        var previous = SampleRollupRecords.Read(tx, partition, request.SeriesSet, request.SeriesId,
            request.From, request.UntilExclusive, charge.Charge);
        Revision(previous, request.ExpectedRevision);
        if (previous is null || previous.Dropped)
        { throw Errors.Fail(ErrorCode.NotFound, SampleRollupProtocol.Missing); }
        var next = previous with
        {
            Revision = checked(previous.Revision + NextRevision),
            Dropped = true,
            Count = AbsentRevision,
            Sum = AbsentRevision,
            Minimum = null,
            Maximum = null
        };
        tx.PutRecord(SampleRollupKeys.Bucket(partition, request.SeriesSet, request.SeriesId,
            request.From, request.UntilExclusive), next);
        return new(SampleRollupProtocol.DropKind, request.SeriesSet, request.SeriesId, next.Revision);
    }

    private static void Scope(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, string set, string series, Capability capability)
    {
        JsonData.Identifier(set);
        JsonData.Identifier(series);
        database.Authorization.Require(principal, partition, set, capability);
        _ = database.Resource(view, partition, set, ResourceKind.TimeSeries);
    }
    private static void Revision(SampleRollupState? previous, long expected)
    {
        if ((previous?.Revision ?? AbsentRevision) != expected)
        { throw Errors.Fail(ErrorCode.RevisionConflict, SampleRollupProtocol.WrongRevision); }
    }
    private static void AdmitBucket(IKeyValueView view, PartitionRef partition, RefreshSampleRollup request,
        int limit, SampleRollupReadCharge charge)
    {
        var scan = view.VisitRange(SampleRollupKeys.Prefix(partition, request.SeriesSet, request.SeriesId), limit,
            static (_, _) => true, observer: charge.Charge);
        if (scan.HasMore || scan.Records >= limit)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, SampleRollupProtocol.BucketBudget); }
    }
}
