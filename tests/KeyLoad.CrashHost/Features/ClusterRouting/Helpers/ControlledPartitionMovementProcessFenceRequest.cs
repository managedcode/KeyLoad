using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Constructs only original unverified inputs for the real A grant and receiver fence producer.</summary>
internal static class ControlledPartitionMovementProcessFenceRequest
{
    private const string GrantIdText = "038edba9-c302-4930-9e6b-18372fb1a958";
    private const string FenceCommandIdText = "e76c270e-2aa6-46bf-979d-1e3a94fe5a66";
    private const string PreparedControlRequired = "The actual Prepared control result is required.";
    private const string AuthorizationGrantRequired = "The actual logged A authorization grant is required.";
    private const string GrantIdentityMismatch = "The original native A grant identity does not match.";
    private const string PrincipalId = "root";
    private const int Version = 1;
    private const int InitialOrdinal = 0;
    internal static readonly Guid GrantId = Guid.Parse(GrantIdText);
    internal static readonly Guid FenceCommandId = Guid.Parse(FenceCommandIdText);

    internal static PartitionMovementTransportRequest Authorize(PartitionMovePhaseResult actualPrepared,
        ControlledPartitionMovementProcessOwners corpus, string callerAddress, DateTimeOffset originalExpiry)
    {
        var control = actualPrepared.Control
            ?? throw new InvalidOperationException(PreparedControlRequired);
        var phase = new PartitionMovePhaseCommand(Version, control.MoveId, control.Partition,
            corpus.Control.Owner, control.SourcePlacement, control.DestinationOwner,
            actualPrepared.Journal.ControlIntentDigest, PartitionMovePeerStage.Fence, InitialOrdinal,
            NativeSerialization.Serialize(new PartitionMoveControlBody(PrincipalId, control)), Resources: []);
        var body = NativeSerialization.Serialize(new PartitionMoveAuthorizeBody(GrantId, FenceCommandId,
            PrincipalId, phase, corpus.Control.Owner, originalExpiry));
        var envelope = new PartitionMovePeerEnvelope(Version, control.MoveId, control.Partition,
            corpus.Control.Owner, control.SourcePlacement, control.DestinationOwner,
            actualPrepared.Journal.ControlIntentDigest, PartitionMovePeerStage.ControlAuthorize,
            InitialOrdinal, originalExpiry, Guid.NewGuid(), body);
        return new(GrantId, envelope, null, corpus.Control.Owner.VoterIds.First(), callerAddress,
            PartitionMovementTransportAction.Apply, Guid.Empty, InitialOrdinal);
    }

    internal static PartitionMovementTransportRequest Fence(PartitionMovePhaseResult actualPrepared,
        PartitionMovePhaseResult actualAuthorization, ControlledPartitionMovementProcessOwners corpus,
        string callerAddress, DateTimeOffset originalExpiry)
    {
        var control = actualPrepared.Control
            ?? throw new InvalidOperationException(PreparedControlRequired);
        var grant = actualAuthorization.Grant
            ?? throw new InvalidOperationException(AuthorizationGrantRequired);
        if (grant.GrantId != GrantId || grant.PhaseCommandId != FenceCommandId)
        { throw new InvalidOperationException(GrantIdentityMismatch); }
        var envelope = new PartitionMovePeerEnvelope(Version, control.MoveId, control.Partition,
            corpus.Control.Owner, control.SourcePlacement, control.DestinationOwner,
            actualPrepared.Journal.ControlIntentDigest, PartitionMovePeerStage.Fence, InitialOrdinal,
            originalExpiry, Guid.NewGuid(), NativeSerialization.Serialize(new PartitionMoveControlBody(PrincipalId, control)), grant);
        return new(FenceCommandId, envelope, actualAuthorization.Journal,
            corpus.Control.Owner.VoterIds.First(), callerAddress, PartitionMovementTransportAction.Apply,
            Guid.Empty, InitialOrdinal);
    }
}
