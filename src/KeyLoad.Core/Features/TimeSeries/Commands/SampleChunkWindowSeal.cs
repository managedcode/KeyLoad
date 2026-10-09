using System.Collections.Immutable;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkWindowSeal
{
    internal static MutationReceipt Execute(DatabaseEngine database, IAtomicTransaction tx,
        PrincipalRecord principal, PartitionRef partition, SealSampleChunkWindow request,
        IOptions<TimeSeriesExecutionOptions> options)
    {
        SampleChunkWindowScope.Require(database, tx, principal, partition, request.SeriesSet, request.SeriesId, request.WindowId);
        var charge = new SampleChunkReadCharge(database.Limits.MaxQueryReadBytes);
        var window = SampleChunkWindowScope.Load(tx, partition, request.SeriesSet, request.SeriesId,
            request.WindowId, request.ExpectedRevision, options, charge);
        if (window.State != SampleChunkWindowState.Open || window.OpenRecords.IsEmpty)
        { throw Errors.Fail(ErrorCode.Conflict, SampleChunkLifecycleProtocol.Invalid); }
        var watermark = SampleRollupRecords.Watermark(tx, partition, request.SeriesSet, request.SeriesId, charge.Charge);
        var next = Seal(database, tx, partition, request.SeriesSet, request.SeriesId,
            window with { SourceSequence = watermark.Sequence, RetentionBeforeUtcTicks = watermark.Floor }, options);
        return new(SampleChunkLifecycleProtocol.SealKind, request.SeriesSet, request.SeriesId, next.Revision);
    }

    internal static SampleChunkWindow Seal(DatabaseEngine database, IAtomicTransaction tx, PartitionRef partition,
        string set, string series, SampleChunkWindow original, IOptions<TimeSeriesExecutionOptions> options)
    {
        foreach (var record in original.OpenRecords) { SampleChunkWindowValidation.Record(record, original, series); }
        var records = original.OpenRecords.OrderBy(record => record.Sample.Timestamp.UtcTicks)
            .ThenBy(record => record.Sequence).ToArray();
        var next = original with { Revision = checked(original.Revision + SampleChunkLifecycleProtocol.First),
            Generation = SampleChunkLifecycleProtocol.First, State = SampleChunkWindowState.Sealed,
            OpenRecords = ImmutableArray<SampleRecord>.Empty, MaintenanceCommandId = Guid.Empty };
        SampleChunkGenerationWriter.Write(tx, partition, set, series, next, records, options, database.Limits.MaxBatchBytes);
        SampleChunkStorage.Write(tx, SampleChunkKeys.Window(partition, set, series, next.WindowId), next, database.Limits.MaxBatchBytes);
        return next;
    }
}
