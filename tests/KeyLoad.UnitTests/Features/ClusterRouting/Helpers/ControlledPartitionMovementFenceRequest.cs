using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Constructs only original unverified inputs for the real A grant and receiver fence producer.</summary>
internal static class ControlledPartitionMovementFenceRequest
{
    private const int Version = 1;
    private const int InitialOrdinal = 0;
    internal static readonly Guid GrantId = Guid.Parse("038edba9-c302-4930-9e6b-18372fb1a958");
    internal static readonly Guid FenceCommandId = Guid.Parse("e76c270e-2aa6-46bf-979d-1e3a94fe5a66");

    internal static PartitionMovementTransportRequest Authorize(PartitionMovePhaseResult actualPrepared,
        ControlledPartitionMovementLoopbackCorpus corpus, string callerAddress, DateTimeOffset originalExpiry)
    {
        var control = actualPrepared.Control
            ?? throw new InvalidOperationException("The actual Prepared control result is required.");
        var phase = new PartitionMovePhaseCommand(Version, control.MoveId, control.Partition,
            corpus.Control.Owner, control.SourcePlacement, control.DestinationOwner,
            actualPrepared.Journal.ControlIntentDigest, PartitionMovePeerStage.Fence, InitialOrdinal,
            ControlledPartitionMovementBodies.Control(control), Resources: []);
        var body = NativeSerialization.Serialize(new PartitionMoveAuthorizeBody(GrantId, FenceCommandId,
            PhysicalShardCatalogFixture.RootPrincipalId, phase, corpus.Control.Owner, originalExpiry));
        var envelope = new PartitionMovePeerEnvelope(Version, control.MoveId, control.Partition,
            corpus.Control.Owner, control.SourcePlacement, control.DestinationOwner,
            actualPrepared.Journal.ControlIntentDigest, PartitionMovePeerStage.ControlAuthorize,
            InitialOrdinal, originalExpiry, Guid.NewGuid(), body);
        return new(GrantId, envelope, null, corpus.Control.Owner.VoterIds.First(), callerAddress,
            PartitionMovementTransportAction.Apply, Guid.Empty, InitialOrdinal);
    }

    internal static PartitionMovementTransportRequest Fence(PartitionMovePhaseResult actualPrepared,
        PartitionMovePhaseResult actualAuthorization, ControlledPartitionMovementLoopbackCorpus corpus,
        string callerAddress, DateTimeOffset originalExpiry)
    {
        var control = actualPrepared.Control
            ?? throw new InvalidOperationException("The actual Prepared control result is required.");
        var grant = actualAuthorization.Grant
            ?? throw new InvalidOperationException("The actual logged A authorization grant is required.");
        if (grant.GrantId != GrantId || grant.PhaseCommandId != FenceCommandId)
        { throw new InvalidOperationException("The original native A grant identity does not match."); }
        var envelope = new PartitionMovePeerEnvelope(Version, control.MoveId, control.Partition,
            corpus.Control.Owner, control.SourcePlacement, control.DestinationOwner,
            actualPrepared.Journal.ControlIntentDigest, PartitionMovePeerStage.Fence, InitialOrdinal,
            originalExpiry, Guid.NewGuid(), ControlledPartitionMovementBodies.Control(control), grant);
        return new(FenceCommandId, envelope, actualAuthorization.Journal,
            corpus.Control.Owner.VoterIds.First(), callerAddress, PartitionMovementTransportAction.Apply,
            Guid.Empty, InitialOrdinal);
    }
}
