using System.Collections.Immutable;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core.TimeSeries;

internal static class SampleChunkWindowDrop
{
    internal static MutationReceipt Execute(DatabaseEngine database, IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef partition, DropSampleChunkWindow request, IOptions<TimeSeriesExecutionOptions> options)
    {
        SampleChunkWindowScope.Require(database, tx, principal, partition, request.SeriesSet, request.SeriesId, request.WindowId);
        var previous = SampleChunkWindowScope.Load(tx, partition, request.SeriesSet, request.SeriesId,
            request.WindowId, request.ExpectedRevision, options);
        var watermark = SampleRollupRecords.Watermark(tx, partition, request.SeriesSet, request.SeriesId);
        if (watermark.Floor is not { } floor || floor < previous.UntilUtcTicks)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, SampleChunkLifecycleProtocol.Invalid);
        }
        if (previous.State == SampleChunkWindowState.Sealed)
        {
            var manifest = SampleChunkManifestReader.Read(tx, partition, request.SeriesSet, request.SeriesId,
                previous, options.Value.MaximumChunkWindowRecords, options);
            _ = SampleChunkGenerationReader.Read(tx, partition, request.SeriesSet, request.SeriesId, previous, manifest, options);
            SampleChunkRepresentationRetirement.Delete(tx, partition, request.SeriesSet, request.SeriesId, previous, manifest);
        }
        var next = previous with
        {
            State = SampleChunkWindowState.Dropped,
            SourceSequence = watermark.Sequence,
            Revision = checked(previous.Revision + SampleChunkLifecycleProtocol.First),
            RetentionBeforeUtcTicks = floor,
            OpenRecords = ImmutableArray<SampleRecord>.Empty,
            CorrectionSequences = ImmutableArray<long>.Empty,
            MaintenanceCommandId = Guid.Empty
        };
        SampleChunkStorage.Write(tx, SampleChunkKeys.Window(partition, request.SeriesSet, request.SeriesId, next.WindowId),
            next);
        return new(SampleChunkLifecycleProtocol.DropKind, request.SeriesSet, request.SeriesId, next.Revision);
    }
}
