using System.Collections.Immutable;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkWindowMerge
{
    internal static MutationReceipt Execute(DatabaseEngine database, IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef partition, MergeSampleChunkWindow request, IOptions<TimeSeriesExecutionOptions> options)
    {
        SampleChunkWindowScope.Require(database, tx, principal, partition, request.SeriesSet, request.SeriesId, request.WindowId);
        var charge = new SampleChunkReadCharge(database.Limits.MaxQueryReadBytes);
        var previous = SampleChunkWindowScope.Load(tx, partition, request.SeriesSet, request.SeriesId,
            request.WindowId, request.ExpectedRevision, options, charge);
        if (previous.State != SampleChunkWindowState.Sealed || previous.CorrectionSequences.IsEmpty)
        { throw Errors.Fail(ErrorCode.Conflict, SampleChunkLifecycleProtocol.Invalid); }
        var manifest = SampleChunkManifestReader.Read(tx, partition, request.SeriesSet, request.SeriesId,
            previous, options.Value.MaximumChunkWindowRecords, charge, options);
        var records = SampleChunkGenerationReader.Read(tx, partition, request.SeriesSet, request.SeriesId, previous,
            manifest, options, charge);
        var watermark = SampleRollupRecords.Watermark(tx, partition, request.SeriesSet, request.SeriesId, charge.Charge);
        var next = previous with { Revision = checked(previous.Revision + SampleChunkLifecycleProtocol.First),
            Generation = checked(previous.Generation + SampleChunkLifecycleProtocol.First),
            SourceSequence = watermark.Sequence, RetentionBeforeUtcTicks = watermark.Floor,
            CorrectionSequences = ImmutableArray<long>.Empty, MaintenanceCommandId = Guid.Empty };
        SampleChunkGenerationWriter.Write(tx, partition, request.SeriesSet, request.SeriesId, next, records,
            options, database.Limits.MaxBatchBytes);
        SampleChunkRepresentationRetirement.Delete(tx, partition, request.SeriesSet, request.SeriesId, previous, manifest);
        SampleChunkStorage.Write(tx, SampleChunkKeys.Window(partition, request.SeriesSet, request.SeriesId, next.WindowId),
            next, database.Limits.MaxBatchBytes);
        return new(SampleChunkLifecycleProtocol.MergeKind, request.SeriesSet, request.SeriesId, next.Revision);
    }
}
