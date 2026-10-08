using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Constructs technical target publication requests only from returned source capability and genuine A authorization.</summary>
internal static class ControlledPartitionMovementTargetPublishRequest
{
    private const int Version = 1;
    private const int ControlOrdinal = 0;

    internal static PartitionMovementTransportRequest Authorize(PartitionMovePhaseResult captured,
        PartitionMovePublishedPlacement actualPublication, ImmutableArray<ResourceDefinition> originalResources, ControlledPartitionMovementLoopbackCorpus corpus,
        string originalCallerAddress, DateTimeOffset originalExpiry)
    {
        var phase = Phase(captured, actualPublication, originalResources, corpus);
        var body = NativeSerialization.Serialize(new PartitionMoveAuthorizeBody(
            ControlledPartitionMovementTargetPhaseIds.PublishGrant(),
            ControlledPartitionMovementTargetPhaseIds.PublishCommand(),
            PhysicalShardCatalogFixture.RootPrincipalId, phase, corpus.Destination.Owner, originalExpiry));
        var envelope = Envelope(phase, PartitionMovePeerStage.ControlAuthorize, ControlOrdinal, originalExpiry, body);
        return new(ControlledPartitionMovementTargetPhaseIds.PublishGrant(), envelope, null,
            corpus.Control.Owner.VoterIds.First(), originalCallerAddress,
            PartitionMovementTransportAction.Apply, Guid.Empty, ControlOrdinal);
    }

    internal static PartitionMovementTransportRequest Publish(PartitionMovePhaseResult captured,
        PartitionMovePublishedPlacement actualPublication, ImmutableArray<ResourceDefinition> originalResources, PartitionMovePhaseResult actualAuthorization,
        ControlledPartitionMovementLoopbackCorpus corpus, string callerAddress, DateTimeOffset originalExpiry)
    {
        var phase = Phase(captured, actualPublication, originalResources, corpus);
        var grant = actualAuthorization.Grant
            ?? throw new InvalidOperationException("The actual original A target-publish grant is absent.");
        if (grant.GrantId != ControlledPartitionMovementTargetPhaseIds.PublishGrant()
            || grant.PhaseCommandId != ControlledPartitionMovementTargetPhaseIds.PublishCommand())
        { throw new InvalidOperationException("The original target-publish grant identity does not match."); }
        var envelope = Envelope(phase, phase.Stage, phase.PageOrdinal, originalExpiry, phase.Body)
            with
        { Grant = grant };
        return new(grant.PhaseCommandId, envelope, actualAuthorization.Journal,
            corpus.Control.Owner.VoterIds.First(), callerAddress, PartitionMovementTransportAction.Apply,
            Guid.Empty, ControlOrdinal);
    }

    private static PartitionMovePhaseCommand Phase(PartitionMovePhaseResult captured,
        PartitionMovePublishedPlacement actualPublication, ImmutableArray<ResourceDefinition> originalResources, ControlledPartitionMovementLoopbackCorpus corpus)
    {
        var control = captured.Control
            ?? throw new InvalidOperationException("The actual Captured control is absent.");
        var body = NativeSerialization.Serialize(new PartitionMovePublishBody(PhysicalShardCatalogFixture.RootPrincipalId,
            control, actualPublication, originalResources));
        return new(Version, control.MoveId, control.Partition, corpus.Control.Owner, control.SourcePlacement,
            corpus.Destination.Owner, captured.Journal.ControlIntentDigest,
            PartitionMovePeerStage.PublishWitness, ControlOrdinal, body, Resources: []);
    }

    private static PartitionMovePeerEnvelope Envelope(PartitionMovePhaseCommand phase,
        PartitionMovePeerStage stage, int ordinal, DateTimeOffset expiry, ReadOnlyMemory<byte> body)
        => new(Version, phase.MoveId, phase.Partition, phase.ControlOwner, phase.SourcePlacement,
            phase.DestinationOwner, phase.ControlIntentDigest, stage, ordinal, expiry, Guid.NewGuid(), body);
}
