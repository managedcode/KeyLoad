using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Constructs target installation requests only from returned source capability and genuine A authorization.</summary>
internal static class ControlledPartitionMovementTargetInstallRequest
{
    private const int Version = 1;
    private const int ControlOrdinal = 0;

    internal static PartitionMovementTransportRequest Authorize(PartitionMovePhaseResult captured,
        PartitionMoveSourceFenceRecord originalFence, PartitionMovementCaptureHandle originalHandle,
        int originalOrdinal, ControlledPartitionMovementLoopbackCorpus corpus,
        string originalCallerAddress, DateTimeOffset originalExpiry)
    {
        var phase = Phase(captured, originalFence, originalHandle, originalOrdinal, corpus);
        var body = NativeSerialization.Serialize(new PartitionMoveAuthorizeBody(
            ControlledPartitionMovementTargetPhaseIds.InstallGrant(originalOrdinal),
            ControlledPartitionMovementTargetPhaseIds.InstallCommand(originalOrdinal),
            PhysicalShardCatalogFixture.RootPrincipalId, phase, corpus.Destination.Owner, originalExpiry));
        var envelope = Envelope(phase, PartitionMovePeerStage.ControlAuthorize, ControlOrdinal, originalExpiry, body);
        return new(ControlledPartitionMovementTargetPhaseIds.InstallGrant(originalOrdinal), envelope, null,
            corpus.Control.Owner.VoterIds.First(), originalCallerAddress,
            PartitionMovementTransportAction.Apply, Guid.Empty, ControlOrdinal);
    }

    internal static PartitionMovementTransportRequest Install(PartitionMovePhaseResult captured,
        PartitionMoveSourceFenceRecord originalFence, PartitionMovementCaptureHandle originalHandle,
        int originalOrdinal, PartitionMovePhaseResult actualAuthorization,
        ControlledPartitionMovementLoopbackCorpus corpus, string callerAddress, DateTimeOffset originalExpiry)
    {
        var phase = Phase(captured, originalFence, originalHandle, originalOrdinal, corpus);
        var grant = actualAuthorization.Grant
            ?? throw new InvalidOperationException("The actual original A target-install grant is absent.");
        if (grant.GrantId != ControlledPartitionMovementTargetPhaseIds.InstallGrant(originalOrdinal)
            || grant.PhaseCommandId != ControlledPartitionMovementTargetPhaseIds.InstallCommand(originalOrdinal))
        { throw new InvalidOperationException("The original target-install grant identity does not match."); }
        var envelope = Envelope(phase, phase.Stage, phase.PageOrdinal, originalExpiry, phase.Body)
            with
        { Grant = grant };
        return new(grant.PhaseCommandId, envelope, actualAuthorization.Journal,
            corpus.Control.Owner.VoterIds.First(), callerAddress, PartitionMovementTransportAction.Apply,
            Guid.Empty, ControlOrdinal);
    }

    private static PartitionMovePhaseCommand Phase(PartitionMovePhaseResult captured,
        PartitionMoveSourceFenceRecord originalFence, PartitionMovementCaptureHandle originalHandle,
        int originalOrdinal, ControlledPartitionMovementLoopbackCorpus corpus)
    {
        var control = captured.Control
            ?? throw new InvalidOperationException("The actual Captured control is absent.");
        var body = NativeSerialization.Serialize(new PartitionMoveInstallBody(PhysicalShardCatalogFixture.RootPrincipalId,
            control, originalFence, originalHandle.Descriptor));
        return new(Version, control.MoveId, control.Partition, corpus.Control.Owner, control.SourcePlacement,
            corpus.Destination.Owner, captured.Journal.ControlIntentDigest,
            PartitionMovePeerStage.Install, originalOrdinal, body, Resources: []);
    }

    private static PartitionMovePeerEnvelope Envelope(PartitionMovePhaseCommand phase,
        PartitionMovePeerStage stage, int ordinal, DateTimeOffset expiry, ReadOnlyMemory<byte> body)
        => new(Version, phase.MoveId, phase.Partition, phase.ControlOwner, phase.SourcePlacement,
            phase.DestinationOwner, phase.ControlIntentDigest, stage, ordinal, expiry, Guid.NewGuid(), body);
}
