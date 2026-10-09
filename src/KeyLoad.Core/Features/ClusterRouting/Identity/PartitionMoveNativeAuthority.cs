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
        if (verified.Stage is PartitionMovePeerStage.ControlCheckpoint or PartitionMovePeerStage.ReceiverIssue
            or PartitionMovePeerStage.RetireCancel
            || verified.Grant is { RequireReceiverIssuance: true }
            || verified.ReceiverIssuanceProof is not null || verified.SourceDispatchWitness is not null)
        { throw Errors.Fail(ErrorCode.PermissionDenied, PartitionMoveProtocol.MissingAuthority); }
        return CreateMovementOperationCore(commandId, localPrincipalId, evaluatedAt, verified);
    }

    internal ReplicatedOperation CreateVerifiedPartitionMovementCheckpointOperation(Guid commandId,
        string localPrincipalId, DateTimeOffset evaluatedAt, PartitionMovePeerEnvelope verified, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        if (verified.Stage != PartitionMovePeerStage.ControlCheckpoint)
        { throw Errors.Fail(ErrorCode.PermissionDenied, PartitionMoveProtocol.MissingAuthority); }
        var owned = verified with { Body = verified.Body.ToArray() };
        PartitionMovePeerEnvelopeValidation.RequireStructure(owned, Limits.MaxBatchBytes);
        RequireCheckpointProofAdmission(localPrincipalId, owned, work);
        var proof = movementCheckpointVerifier.Verify(this, localPrincipalId, owned.Body, work);
        var expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(owned.Body.Span));
        if (proof != expected)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMoveProtocol.MissingAuthority); }
        var operation = CreateMovementOperationCore(commandId, localPrincipalId, evaluatedAt, owned);
        work.Check();
        return operation;
    }

    private ReplicatedOperation CreateMovementOperationCore(Guid commandId, string localPrincipalId,
        DateTimeOffset evaluatedAt, PartitionMovePeerEnvelope verified, PartitionMoveReceiverEffectAdmission? admission = null)
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
                ?? System.Collections.Immutable.ImmutableArray<ResourceDefinition>.Empty, admission);
        if (PartitionMoveGrantValidation.IsLocalControl(command.Stage)
            && command.Stage != PartitionMovePeerStage.ControlCheckpoint)
        {
            Store.Read(view =>
            {
                var principal = RequireMoveDispatchPrincipal(view, localPrincipalId, EvaluationClock.GetUtcNow());
                RequireMoveParentLocalEffectAdmission(view, principal, commandId, command);
                return true;
            });
        }
        if (!PartitionMoveGrantValidation.IsLocalControl(command.Stage))
        {
            Store.Read(view =>
            {
                var principal = RequireMoveDispatchPrincipal(view, localPrincipalId, EvaluationClock.GetUtcNow());
                RequireMoveReceiverEffectAdmission(view, principal, commandId, command, evaluatedAt);
                return true;
            });
        }
        if (command.Stage == PartitionMovePeerStage.ControlPrepare)
        {
            Store.Read(view =>
            {
                var principal = Principal(view, localPrincipalId, EvaluationClock.GetUtcNow());
                if (!principal.ClusterAdministrator)
                { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
                RequireMovePrepareNotCancelled(view, principal, commandId, command);
                return true;
            });
        }
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
                or PartitionMovePeerStage.ControlFinalizeCommand or PartitionMovePeerStage.ControlCheckpoint => request.ControlOwner.Incarnation,
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
                or PartitionMovePeerStage.ControlFinalizeCommand or PartitionMovePeerStage.ControlCheckpoint => request.ControlOwner.PhysicalShardId,
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
