using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ReplicatedOperation CreateVerifiedPartitionMovementOperation(Guid commandId,
        string localPrincipalId, DateTimeOffset evaluatedAt, PartitionMovePeerEnvelope verified)
    {
        PartitionMovePeerEnvelopeValidation.RequireStructure(verified, Limits.MaxBatchBytes);
        if (commandId == Guid.Empty || string.IsNullOrWhiteSpace(localPrincipalId)
            || verified.ExpiresAt <= EvaluationClock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        RequireMovementReceiver(verified);
        RequireControlledCommandPhaseIdentity(commandId, verified);
        if (!PartitionMoveGrantValidation.IsLocalControl(verified.Stage))
        {
            var receiver = Store.Read(view => PhysicalShardCatalogRecordSerialization.Read(view))
                ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
            PartitionMoveGrantValidation.Require(verified, commandId, receiver.DefaultShard,
                EvaluationClock.GetUtcNow());
            PartitionMoveResourceBinding.Require(verified);
        }
        var command = new PartitionMovePhaseCommand(verified.Version, verified.MoveId, verified.Partition,
            verified.ControlOwner, verified.SourcePlacement, verified.DestinationOwner,
            verified.ControlIntentDigest, verified.Stage, verified.PageOrdinal, verified.Body, verified.Grant?.GrantId, verified.Grant?.Resources
                ?? System.Collections.Immutable.ImmutableArray<ResourceDefinition>.Empty);
        var identity = MovementPhaseIdentityJson(command);
        var payload = NativeSerialization.Serialize(command);
        return IssueNativeOperation(new(commandId, OperationKind.PartitionMovementPhase, localPrincipalId,
            evaluatedAt, identity), new NativeCommandPayload(payload));
    }

    private void RequireMovementReceiver(PartitionMovePeerEnvelope request)
    {
        RequireConfiguredMovementOwner();
        var expected = request.Stage switch
        {
            PartitionMovePeerStage.ControlPrepare or PartitionMovePeerStage.ControlAdvance
                or PartitionMovePeerStage.ControlFinalize or PartitionMovePeerStage.ControlAuthorize
                or PartitionMovePeerStage.ControlAcknowledge or PartitionMovePeerStage.ControlAcceptFence
                or PartitionMovePeerStage.ControlBeginAbort or PartitionMovePeerStage.ControlFinalizeAbort
                or PartitionMovePeerStage.ControlCompleteRetirement or PartitionMovePeerStage.ControlCancelGrants
                or PartitionMovePeerStage.ControlAdmitCommand or PartitionMovePeerStage.ControlAcknowledgeCommand
                or PartitionMovePeerStage.ControlFinalizeCommand => request.ControlOwner.Incarnation,
            PartitionMovePeerStage.Fence or PartitionMovePeerStage.Capture or PartitionMovePeerStage.Retire
                or PartitionMovePeerStage.SourceBeginAbort
                => request.SourcePlacement.Incarnation,
            PartitionMovePeerStage.Abort => Store.Identity.Incarnation,
            _ => request.DestinationOwner.Incarnation
        };
        var catalog = Store.Read(view => PhysicalShardCatalogRecordSerialization.Read(view))
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        var physicalId = request.Stage switch
        {
            PartitionMovePeerStage.ControlPrepare or PartitionMovePeerStage.ControlAdvance
                or PartitionMovePeerStage.ControlFinalize or PartitionMovePeerStage.ControlAuthorize
                or PartitionMovePeerStage.ControlAcknowledge or PartitionMovePeerStage.ControlAcceptFence
                or PartitionMovePeerStage.ControlBeginAbort or PartitionMovePeerStage.ControlFinalizeAbort
                or PartitionMovePeerStage.ControlCompleteRetirement or PartitionMovePeerStage.ControlCancelGrants
                or PartitionMovePeerStage.ControlAdmitCommand or PartitionMovePeerStage.ControlAcknowledgeCommand
                or PartitionMovePeerStage.ControlFinalizeCommand => request.ControlOwner.PhysicalShardId,
            PartitionMovePeerStage.Fence or PartitionMovePeerStage.Capture or PartitionMovePeerStage.Retire
                or PartitionMovePeerStage.SourceBeginAbort
                => request.SourcePlacement.PhysicalShardId,
            PartitionMovePeerStage.Abort => expected == request.SourcePlacement.Incarnation
                ? request.SourcePlacement.PhysicalShardId : request.DestinationOwner.PhysicalShardId,
            _ => request.DestinationOwner.PhysicalShardId
        };
        if (catalog.DefaultShard.PhysicalShardId != physicalId || expected != Store.Identity.Incarnation || catalog.DefaultShard.Incarnation != expected
            || request.Stage == PartitionMovePeerStage.Abort
                && expected != request.SourcePlacement.Incarnation && expected != request.DestinationOwner.Incarnation)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }
}
