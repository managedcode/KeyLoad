using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Constructs source-retirement requests only from returned source capability and genuine A authorization.</summary>
internal static class ControlledPartitionMovementRetireRequest
{
    private const int Version = 1;
    private const int ControlOrdinal = 0;

    internal static PartitionMovementTransportRequest Authorize(PartitionMovePhaseResult captured,
        PartitionMovePublishedPlacement actualPublication, int familyOrdinal, int batchOrdinal, ControlledPartitionMovementLoopbackCorpus corpus,
        string originalCallerAddress, DateTimeOffset originalExpiry)
    {
        var phase = Phase(captured, actualPublication, familyOrdinal, batchOrdinal, corpus);
        var body = NativeSerialization.Serialize(new PartitionMoveAuthorizeBody(
            ControlledPartitionMovementRetirePhaseIds.Grant(batchOrdinal),
            ControlledPartitionMovementRetirePhaseIds.Command(batchOrdinal),
            PhysicalShardCatalogFixture.RootPrincipalId, phase, corpus.Control.Owner, originalExpiry));
        var envelope = Envelope(phase, PartitionMovePeerStage.ControlAuthorize, ControlOrdinal, originalExpiry, body);
        return new(ControlledPartitionMovementRetirePhaseIds.Grant(batchOrdinal), envelope, null,
            corpus.Control.Owner.VoterIds.First(), originalCallerAddress,
            PartitionMovementTransportAction.Apply, Guid.Empty, ControlOrdinal);
    }

    internal static PartitionMovementTransportRequest Retire(PartitionMovePhaseResult captured,
        PartitionMovePublishedPlacement actualPublication, int familyOrdinal, int batchOrdinal, PartitionMovePhaseResult actualAuthorization,
        ControlledPartitionMovementLoopbackCorpus corpus, string callerAddress, DateTimeOffset originalExpiry)
    {
        var phase = Phase(captured, actualPublication, familyOrdinal, batchOrdinal, corpus);
        var grant = actualAuthorization.Grant
            ?? throw new InvalidOperationException("The actual original A source-retire grant is absent.");
        if (grant.GrantId != ControlledPartitionMovementRetirePhaseIds.Grant(batchOrdinal)
            || grant.PhaseCommandId != ControlledPartitionMovementRetirePhaseIds.Command(batchOrdinal))
        { throw new InvalidOperationException("The original source-retire grant identity does not match."); }
        var envelope = Envelope(phase, phase.Stage, phase.PageOrdinal, originalExpiry, phase.Body)
            with
        { Grant = grant };
        return new(grant.PhaseCommandId, envelope, actualAuthorization.Journal,
            corpus.Control.Owner.VoterIds.First(), callerAddress, PartitionMovementTransportAction.Apply,
            Guid.Empty, ControlOrdinal);
    }

    private static PartitionMovePhaseCommand Phase(PartitionMovePhaseResult captured,
        PartitionMovePublishedPlacement actualPublication, int familyOrdinal, int batchOrdinal, ControlledPartitionMovementLoopbackCorpus corpus)
    {
        var control = captured.Control
            ?? throw new InvalidOperationException("The actual Captured control is absent.");
        var body = NativeSerialization.Serialize(new PartitionMoveCleanupBody(PhysicalShardCatalogFixture.RootPrincipalId,
            control, PartitionMoveCleanupRole.Source, familyOrdinal,
            ControlledPartitionMovementTargetPhaseIds.PublishGrant(), actualPublication));
        return new(Version, control.MoveId, control.Partition, corpus.Control.Owner, control.SourcePlacement,
            corpus.Destination.Owner, captured.Journal.ControlIntentDigest,
            PartitionMovePeerStage.Retire, batchOrdinal, body, Resources: []);
    }

    private static PartitionMovePeerEnvelope Envelope(PartitionMovePhaseCommand phase,
        PartitionMovePeerStage stage, int ordinal, DateTimeOffset expiry, ReadOnlyMemory<byte> body)
        => new(Version, phase.MoveId, phase.Partition, phase.ControlOwner, phase.SourcePlacement,
            phase.DestinationOwner, phase.ControlIntentDigest, stage, ordinal, expiry, Guid.NewGuid(), body);
}
