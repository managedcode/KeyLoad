using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Constructs immutable abort bodies; only actual A authorization supplies receiver authority.</summary>
internal static class ControlledPartitionMovementAbortRequest
{
    private const int Version = 1;
    private const int ControlOrdinal = 0;

    internal static PartitionMovementTransportRequest Authorize(PartitionMovePhaseResult actualAborting,
        PartitionMovePeerStage stage, PartitionMoveCleanupRole role, int familyOrdinal, int batchOrdinal,
        Guid actualPrecedingGrantId, ControlledPartitionMovementLoopbackCorpus corpus,
        string originalCallerAddress, DateTimeOffset originalExpiry)
    {
        var phase = Phase(actualAborting, stage, role, familyOrdinal, batchOrdinal,
            actualPrecedingGrantId, corpus);
        var receiver = role == PartitionMoveCleanupRole.Source ? corpus.Control.Owner : corpus.Destination.Owner;
        var body = NativeSerialization.Serialize(new PartitionMoveAuthorizeBody(
            ControlledPartitionMovementAbortPhaseIds.Grant(stage, role, batchOrdinal),
            ControlledPartitionMovementAbortPhaseIds.Command(stage, role, batchOrdinal),
            PhysicalShardCatalogFixture.RootPrincipalId, phase, receiver, originalExpiry));
        return new(ControlledPartitionMovementAbortPhaseIds.Grant(stage, role, batchOrdinal),
            Envelope(phase, PartitionMovePeerStage.ControlAuthorize, ControlOrdinal, originalExpiry, body),
            null, corpus.Control.Owner.VoterIds.First(), originalCallerAddress,
            PartitionMovementTransportAction.Apply, Guid.Empty, ControlOrdinal);
    }

    internal static PartitionMovementTransportRequest Apply(PartitionMovePhaseResult actualAborting,
        PartitionMovePeerStage stage, PartitionMoveCleanupRole role, int familyOrdinal, int batchOrdinal,
        Guid actualPrecedingGrantId, PartitionMovePhaseResult actualAuthorization,
        ControlledPartitionMovementLoopbackCorpus corpus, string originalCallerAddress,
        DateTimeOffset originalExpiry)
    {
        var phase = Phase(actualAborting, stage, role, familyOrdinal, batchOrdinal,
            actualPrecedingGrantId, corpus);
        var grant = actualAuthorization.Grant
            ?? throw new InvalidOperationException("The genuine A abort phase grant is absent.");
        if (grant.GrantId != ControlledPartitionMovementAbortPhaseIds.Grant(stage, role, batchOrdinal)
            || grant.PhaseCommandId != ControlledPartitionMovementAbortPhaseIds.Command(stage, role, batchOrdinal))
        { throw new InvalidOperationException("The original abort grant/command identity does not match."); }
        return new(grant.PhaseCommandId,
            Envelope(phase, stage, batchOrdinal, originalExpiry, phase.Body) with { Grant = grant },
            actualAuthorization.Journal, corpus.Control.Owner.VoterIds.First(), originalCallerAddress,
            PartitionMovementTransportAction.Apply, Guid.Empty, ControlOrdinal);
    }

    private static PartitionMovePhaseCommand Phase(PartitionMovePhaseResult actualAborting,
        PartitionMovePeerStage stage, PartitionMoveCleanupRole role, int familyOrdinal, int batchOrdinal,
        Guid actualPrecedingGrantId, ControlledPartitionMovementLoopbackCorpus corpus)
    {
        var control = actualAborting.Control
            ?? throw new InvalidOperationException("The actual original Aborting control is absent.");
        var body = NativeSerialization.Serialize(new PartitionMoveCleanupBody(
            PhysicalShardCatalogFixture.RootPrincipalId, control, role, familyOrdinal,
            actualPrecedingGrantId));
        return new(Version, control.MoveId, control.Partition, corpus.Control.Owner,
            control.SourcePlacement, corpus.Destination.Owner, actualAborting.Journal.ControlIntentDigest,
            stage, batchOrdinal, body, Resources: []);
    }

    private static PartitionMovePeerEnvelope Envelope(PartitionMovePhaseCommand phase,
        PartitionMovePeerStage stage, int ordinal, DateTimeOffset originalExpiry, ReadOnlyMemory<byte> body)
        => new(Version, phase.MoveId, phase.Partition, phase.ControlOwner, phase.SourcePlacement,
            phase.DestinationOwner, phase.ControlIntentDigest, stage, ordinal, originalExpiry, Guid.NewGuid(), body);
}
