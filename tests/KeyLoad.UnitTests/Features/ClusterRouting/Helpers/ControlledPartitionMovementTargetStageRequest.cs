using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Constructs target staging requests only from returned source capability and genuine A authorization.</summary>
internal static class ControlledPartitionMovementTargetStageRequest
{
    private const int Version = 1;
    private const int ControlOrdinal = 0;

    internal static PartitionMovementTransportRequest Authorize(PartitionMovePhaseResult captured,
        PartitionMoveSourceFenceRecord originalFence, PartitionMovementCaptureHandle originalHandle,
        PartitionMovementPageResult originalPage, ControlledPartitionMovementLoopbackCorpus corpus,
        string originalCallerAddress, DateTimeOffset originalExpiry)
    {
        var phase = Phase(captured, originalFence, originalHandle, originalPage, corpus);
        var body = NativeSerialization.Serialize(new PartitionMoveAuthorizeBody(
            ControlledPartitionMovementTargetPhaseIds.Grant(originalPage.Ordinal),
            ControlledPartitionMovementTargetPhaseIds.Command(originalPage.Ordinal),
            PhysicalShardCatalogFixture.RootPrincipalId, phase, corpus.Destination.Owner, originalExpiry));
        var envelope = Envelope(phase, PartitionMovePeerStage.ControlAuthorize, ControlOrdinal, originalExpiry, body);
        return new(ControlledPartitionMovementTargetPhaseIds.Grant(originalPage.Ordinal), envelope, null,
            corpus.Control.Owner.VoterIds.First(), originalCallerAddress,
            PartitionMovementTransportAction.Apply, Guid.Empty, ControlOrdinal);
    }

    internal static PartitionMovementTransportRequest Stage(PartitionMovePhaseResult captured,
        PartitionMoveSourceFenceRecord originalFence, PartitionMovementCaptureHandle originalHandle,
        PartitionMovementPageResult originalPage, PartitionMovePhaseResult actualAuthorization,
        ControlledPartitionMovementLoopbackCorpus corpus, string callerAddress, DateTimeOffset originalExpiry)
    {
        var phase = Phase(captured, originalFence, originalHandle, originalPage, corpus);
        var grant = actualAuthorization.Grant
            ?? throw new InvalidOperationException("The actual original A target-stage grant is absent.");
        if (grant.GrantId != ControlledPartitionMovementTargetPhaseIds.Grant(originalPage.Ordinal)
            || grant.PhaseCommandId != ControlledPartitionMovementTargetPhaseIds.Command(originalPage.Ordinal))
        { throw new InvalidOperationException("The original target-stage grant identity does not match."); }
        var envelope = Envelope(phase, phase.Stage, phase.PageOrdinal, originalExpiry, phase.Body)
            with
        { Grant = grant };
        return new(grant.PhaseCommandId, envelope, actualAuthorization.Journal,
            corpus.Control.Owner.VoterIds.First(), callerAddress, PartitionMovementTransportAction.Apply,
            Guid.Empty, ControlOrdinal);
    }

    private static PartitionMovePhaseCommand Phase(PartitionMovePhaseResult captured,
        PartitionMoveSourceFenceRecord originalFence, PartitionMovementCaptureHandle originalHandle,
        PartitionMovementPageResult originalPage, ControlledPartitionMovementLoopbackCorpus corpus)
    {
        var control = captured.Control
            ?? throw new InvalidOperationException("The actual Captured control is absent.");
        var page = NativeSerialization.Deserialize<PartitionMoveImagePage>(originalPage.NativePage.Span);
        var body = NativeSerialization.Serialize(new PartitionMovePageBody(PhysicalShardCatalogFixture.RootPrincipalId,
            control, originalFence, originalHandle.Descriptor, page));
        return new(Version, control.MoveId, control.Partition, corpus.Control.Owner, control.SourcePlacement,
            corpus.Destination.Owner, captured.Journal.ControlIntentDigest,
            PartitionMovePeerStage.StagePage, originalPage.Ordinal, body, Resources: []);
    }

    private static PartitionMovePeerEnvelope Envelope(PartitionMovePhaseCommand phase,
        PartitionMovePeerStage stage, int ordinal, DateTimeOffset expiry, ReadOnlyMemory<byte> body)
        => new(Version, phase.MoveId, phase.Partition, phase.ControlOwner, phase.SourcePlacement,
            phase.DestinationOwner, phase.ControlIntentDigest, stage, ordinal, expiry, Guid.NewGuid(), body);
}
