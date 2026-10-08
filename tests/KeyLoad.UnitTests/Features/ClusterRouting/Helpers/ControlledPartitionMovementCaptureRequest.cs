using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Constructs only downward-bounded original inputs; actual native A authorization remains mandatory.</summary>
internal static class ControlledPartitionMovementCaptureRequest
{
    private const int Version = 1;
    private const int InitialOrdinal = 0;
    private const int PageFrames = 2;
    private const int RecordOverhead = 512;
    private const int SessionOverhead = 4096;
    internal static readonly Guid GrantId = Guid.Parse("d9e41718-70a8-40ba-a01c-7fece1a5f606");
    internal static readonly Guid CaptureCommandId = Guid.Parse("46040f24-1614-4014-bf35-0fae4c6ab9fd");

    internal static PartitionMovementTransportRequest Authorize(DatabaseEngine source, ServerRuntimeOptions runtime,
        PartitionMovePhaseResult actualAcceptedFence, ControlledPartitionMovementLoopbackCorpus corpus,
        string originalCallerAddress, DateTimeOffset originalExpiry)
    {
        var control = actualAcceptedFence.Control
            ?? throw new InvalidOperationException("The actual Fenced control is required.");
        var body = Body(source, runtime, actualAcceptedFence);
        var phase = new PartitionMovePhaseCommand(Version, control.MoveId, control.Partition,
            corpus.Control.Owner, control.SourcePlacement, corpus.Destination.Owner,
            actualAcceptedFence.Journal.ControlIntentDigest, PartitionMovePeerStage.Capture, InitialOrdinal, body, Resources: []);
        var authorize = NativeSerialization.Serialize(new PartitionMoveAuthorizeBody(GrantId, CaptureCommandId,
            PhysicalShardCatalogFixture.RootPrincipalId, phase, corpus.Control.Owner, originalExpiry));
        return new(GrantId, Envelope(actualAcceptedFence, corpus, PartitionMovePeerStage.ControlAuthorize,
            originalExpiry, authorize), null, corpus.Control.Owner.VoterIds.First(), originalCallerAddress,
            PartitionMovementTransportAction.Apply, Guid.Empty, InitialOrdinal);
    }

    internal static PartitionMovementTransportRequest Capture(DatabaseEngine source, ServerRuntimeOptions runtime,
        PartitionMovePhaseResult actualAcceptedFence, PartitionMovePhaseResult actualAuthorization,
        ControlledPartitionMovementLoopbackCorpus corpus, string originalCallerAddress, DateTimeOffset originalExpiry)
    {
        var grant = actualAuthorization.Grant
            ?? throw new InvalidOperationException("The actual logged A Capture grant is required.");
        if (grant.GrantId != GrantId || grant.PhaseCommandId != CaptureCommandId)
        { throw new InvalidOperationException("The actual original Capture grant identity does not match."); }
        var envelope = Envelope(actualAcceptedFence, corpus, PartitionMovePeerStage.Capture, originalExpiry,
            Body(source, runtime, actualAcceptedFence)) with
        { Grant = grant };
        return new(CaptureCommandId, envelope, actualAuthorization.Journal,
            corpus.Control.Owner.VoterIds.First(), originalCallerAddress, PartitionMovementTransportAction.Capture,
            Guid.Empty, InitialOrdinal);
    }

    private static byte[] Body(DatabaseEngine source, ServerRuntimeOptions runtime, PartitionMovePhaseResult actualAcceptedFence)
    {
        var fence = actualAcceptedFence.Fence
            ?? throw new InvalidOperationException("The actual settled native source fence is required.");
        var memory = runtime.Core.CacheMemory.Value;
        var records = Math.Min(source.Limits.MaxScanRecords, memory.MaxRetainedEntries);
        var resourceBytes = source.Limits.MaxBatchBytes;
        var overhead = checked((long)records * RecordOverhead
            + (long)PartitionRecordFamilies.All.Length * RecordOverhead + PageFrames * resourceBytes + SessionOverhead);
        var available = checked(memory.MaxRetainedBytes - overhead);
        if (available <= 0)
        { throw new InvalidOperationException("The configured movement capture ceiling is insufficient."); }
        var imageBytes = Math.Min(Math.Min(source.Limits.MaxBatchBytes, source.Limits.MaxQueryReadBytes),
            available / PageFrames);
        var pageBytes = checked((int)(imageBytes / PageFrames));
        if (imageBytes <= 0 || pageBytes <= 0)
        { throw new InvalidOperationException("The configured movement capture page ceiling is insufficient."); }
        return NativeSerialization.Serialize(new PartitionMoveCaptureRequest(
            PhysicalShardCatalogFixture.RootPrincipalId, fence, pageBytes, imageBytes, records));
    }

    private static PartitionMovePeerEnvelope Envelope(PartitionMovePhaseResult actualAcceptedFence,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePeerStage stage,
        DateTimeOffset originalExpiry, byte[] body)
    {
        var control = actualAcceptedFence.Control
            ?? throw new InvalidOperationException("The actual Fenced control is required.");
        return new(Version, control.MoveId, control.Partition, corpus.Control.Owner, control.SourcePlacement,
            corpus.Destination.Owner, actualAcceptedFence.Journal.ControlIntentDigest, stage, InitialOrdinal,
            originalExpiry, Guid.NewGuid(), body);
    }
}
