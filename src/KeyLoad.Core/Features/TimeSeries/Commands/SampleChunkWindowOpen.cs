using System.Collections.Immutable;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkWindowOpen
{
    internal static MutationReceipt Execute(DatabaseEngine database, IAtomicTransaction tx,
        PrincipalRecord principal, PartitionRef partition, OpenSampleChunkWindow request,
        IOptions<TimeSeriesExecutionOptions> options)
    {
        SampleChunkWindowScope.Require(database, tx, principal, partition, request.SeriesSet, request.SeriesId, request.WindowId);
        SampleRollupValidation.Range(request.From, request.Until);
        if (options.Value.MaximumChunkWindowRecords > database.Limits.MaxScanRecords)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, SampleChunkLifecycleProtocol.Exhausted); }
        var charge = new SampleChunkReadCharge(database.Limits.MaxQueryReadBytes);
        var watermark = SampleRollupRecords.Watermark(tx, partition, request.SeriesSet, request.SeriesId, charge.Charge);
        if (watermark.Floor is { } floor && request.From.UtcTicks < floor)
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, SampleChunkLifecycleProtocol.Missing); }
        var windows = SampleChunkStorage.Windows(tx, partition, request.SeriesSet, request.SeriesId, options.Value, charge);
        if (windows.Count >= options.Value.MaximumChunkWindows)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, SampleChunkLifecycleProtocol.Exhausted); }
        if (windows.Any(window => window.WindowId == request.WindowId || window.State != SampleChunkWindowState.Dropped
            && window.FromUtcTicks < request.Until.UtcTicks && window.UntilUtcTicks > request.From.UtcTicks))
        { throw Errors.Fail(ErrorCode.Conflict, SampleChunkLifecycleProtocol.Existing); }
        RequireEmpty(tx, partition, request, charge);
        var window = new SampleChunkWindow(SampleChunkLifecycleProtocol.Version, request.WindowId,
            request.From.UtcTicks, request.Until.UtcTicks, SampleChunkLifecycleProtocol.First,
            SampleChunkLifecycleProtocol.Absent, SampleChunkWindowState.Open, principal.Id, watermark.Sequence,
            ImmutableArray<SampleRecord>.Empty, ImmutableArray<long>.Empty, Guid.Empty, watermark.Floor);
        SampleChunkStorage.Write(tx, SampleChunkKeys.Window(partition, request.SeriesSet, request.SeriesId, request.WindowId),
            window, database.Limits.MaxBatchBytes);
        return new(SampleChunkLifecycleProtocol.OpenKind, request.SeriesSet, request.SeriesId, window.Revision);
    }

    private static void RequireEmpty(IKeyValueView view, PartitionRef partition, OpenSampleChunkWindow request,
        SampleChunkReadCharge charge)
    {
        var found = false;
        view.VisitRange(SampleReadKeys.Prefix(partition, request.SeriesSet, request.SeriesId),
            checked((int)SampleChunkLifecycleProtocol.First), (_, _) => { found = true; return false; },
            SampleReadKeys.FromInclusive(partition, request.SeriesSet, request.SeriesId, request.From),
            SampleReadKeys.UntilExclusive(partition, request.SeriesSet, request.SeriesId, request.Until), charge.Charge);
        if (found)
        { throw Errors.Fail(ErrorCode.Conflict, SampleChunkLifecycleProtocol.Existing); }
    }
}
