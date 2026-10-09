using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementClientParentAdmissionOperations
{
    internal static async Task RequireParentSenderAsync(PartitionMovementClient owner, PartitionMoveParentPhase original,
        PartitionMoveReceiverSourceWitness dispatch, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        await owner.partition.Coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        var grant = original.OriginalGrant
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var principal = owner.partition.Database.Store.Read(view => owner.partition.Database.Principal(view,
            grant.OperatorPrincipalId, owner.clock.GetUtcNow()));
        GrainRequestAuthority.RequireAdministrator(principal);
        if (principal.PolicyEpoch != grant.OperatorPolicyEpoch || !grant.RequireReceiverIssuance)
        { throw Errors.Fail(ErrorCode.PermissionDenied, PartitionMovementProtocol.InvalidProof); }
        var envelope = PartitionMovementParentCheckpointOwner.OriginalEnvelope(original, release: false);
        var authorization = original.OriginalAuthorization
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var body = new PartitionMoveReceiverIssueBody(PartitionMoveProtocol.Version,
            original.OriginalPhaseCommandId, envelope, authorization, dispatch.OriginalReplyBytes,
            SourcePendingSignature: dispatch.OriginalReplySignature);
        PartitionMovementReceiverSourceProofVerifier.Require(owner.partition.Database, body, work, owner.options.Value, owner.partition.Configuration);
        var raw = NativeSerialization.Deserialize<PartitionMovementSourcePendingReply>(dispatch.OriginalReplyBytes.Span);
        var signed = GrainNativePayload.Read<GrainValue>(raw.Reply.Payload).Value as PartitionMovementParentStateResult
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        PartitionMovementClientParentAdmissionOperations.RequireCurrentParentSender(owner, principal.Id, original, signed.State, work);
    }

    internal static void RequireCurrentParentSender(PartitionMovementClient owner, string principalId, PartitionMoveParentPhase original,
        PartitionMoveParentState signed, ReadExecutionBudget work)
    {
        var header = signed.Header
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var read = work.CreateReadGrant(work.RemainingReadGrantBytes, work.RemainingReadGrantRecords);
        var current = owner.partition.Database.ReadPartitionMovementParentState(principalId,
            header.OriginalTransferRequest, original.OriginalPhaseCommandId, work, read);
        work.CompleteReadGrant(read);
        var local = PhysicalOwnerConfiguredTuples.Local(owner.options.Value, owner.partition);
        if (current.Header?.Generation != header.Generation || current.CurrentReadCut < signed.CurrentReadCut
            || !PhysicalOwnerEntryValidation.SameOwner(local.Owner, header.ControlOwner)
            || current.Pending is null || current.Pending.OriginalResult is not null
            || current.Pending.ReceiverSourceCheckpointReceipt is null
            || current.Pending.ReceiverIssuePacketCheckpointReceipt is null
            || current.Pending.ReceiverIssuanceCheckpointReceipt is null
            || !NativeSerialization.Serialize(current.Pending).AsSpan().SequenceEqual(NativeSerialization.Serialize(signed.Pending))
            || !NativeSerialization.Serialize(current.Pending).AsSpan().SequenceEqual(NativeSerialization.Serialize(original)))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        work.Check();
    }
}
