using KeyLoad.Core;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorFirstAdmitPreparation
{
    private const int Version = 1;
    private const long InitialRevision = 0;

    internal static ReplicatedOperation Create(PartitionHost partition, TimeProvider clock,
        DecodedGrainRequest parent, EventVectorCapturedExchange[] originalCaptures,
        EventVectorAdmissionPolicy admission, EventVectorRemoteEncoding encoding,
        ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(work);
        originalCancellationToken.ThrowIfCancellationRequested();
        work.Check();
        var envelope = parent.Envelope;
        if (envelope.Purpose != EventVectorRequestScope.ParentPurpose
            || envelope.CommandKind != OperationKind.EventFeedControl || envelope.ReadKind is not null
            || envelope.RequestId == Guid.Empty || envelope.ExpiresAt <= clock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.TokenInvalidated, EventVectorRemoteProtocol.Invalid); }
        var database = partition.Database;
        var principal = GrainRequestAuthority.ReloadForRequest(database, envelope, clock);
        var original = GrainNativePayload.ReadCommand<EventFeedControlRequest>(parent.Payload);
        if (original.Action != EventFeedControlAction.Open || envelope.CommandId != original.CommandId)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, EventVectorRemoteProtocol.Invalid); }
        var directory = database.ReadEventVectorConfiguredOwners(principal.Id, original,
            work, originalCancellationToken);
        var entries = EventVectorFirstCaptureSet.Read(directory, original, principal.Id,
            originalCaptures, admission, work);
        using var witness = EventVectorFirstWitnessOwner.Create(entries, originalCaptures,
            admission, encoding, work);
        var phase = new EventVectorControlPhase(Version, original, EventVectorControlAction.AdmitFirstIntent,
            InitialRevision, InitialRevision, InitialRevision, null, null, null,
            ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty)
        {
            OriginalParentExpiresAt = envelope.ExpiresAt,
            OriginalParentRequestId = envelope.RequestId,
            OriginalCoverageWitness = witness.OriginalWitnessBytes
        };
        originalCancellationToken.ThrowIfCancellationRequested();
        work.Check();
        return database.CreateVerifiedEventVectorControlPhase(principal.Id, phase, work);
    }
}
