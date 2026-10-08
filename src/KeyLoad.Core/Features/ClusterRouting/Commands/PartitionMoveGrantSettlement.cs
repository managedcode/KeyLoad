using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMovePhaseResult ExecuteMoveAcknowledge(IAtomicTransaction transaction,
        PrincipalRecord principal, Guid commandId, PartitionMovePhaseCommand phase, long position)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveAcknowledgeBody>(phase.Body.Span);
        var grant = PartitionMoveGrantStorage.Read(transaction, phase.Partition, body.GrantId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var receipt = body.Settlement;
        if (grant.AbortDisposition is not null || grant.OperatorPrincipalId != principal.Id || grant.OperatorPolicyEpoch != principal.PolicyEpoch
            || grant.MoveId != phase.MoveId || grant.ControlIntentDigest != phase.ControlIntentDigest
            || receipt.CommandId != grant.PhaseCommandId
            || receipt.AppliedPosition <= PartitionMoveProtocol.EmptyCount
            || receipt.ControlIntentDigest != grant.ControlIntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(receipt.PhysicalOwner, grant.ReceiverOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        if (grant.Settlement is { } previous)
        {
            if (previous.CommandId != receipt.CommandId || previous.AppliedPosition != receipt.AppliedPosition
                || previous.ControlIntentDigest != receipt.ControlIntentDigest
                || !PhysicalOwnerEntryValidation.SameOwner(previous.PhysicalOwner, receipt.PhysicalOwner))
            { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        }
        else
        {
            grant = grant with { Settlement = receipt };
            PartitionMoveGrantStorage.Write(transaction, grant, Limits.MaxBatchBytes);
            PartitionMoveGrantStorage.ChangeOutstanding(transaction,
                PartitionMoveGrantStorage.MoveCountKey(phase.Partition, phase.MoveId), false, Limits.MaxBatchMutations);
            PartitionMoveGrantStorage.ChangeOutstanding(transaction, principal.Id, false, Limits.MaxBatchMutations);
            PartitionMoveGrantStorage.ChangeOutstanding(transaction,
                PartitionMoveGrantStorage.DatabaseKey(phase.Partition.TenantId, phase.Partition.DatabaseId),
                false, Limits.MaxBatchMutations);
        }
        return new(phase.MoveId, phase.Stage, MoveJournalReceipt(transaction, commandId, position,
            phase.ControlIntentDigest), null, null, null, null, grant);
    }
}
