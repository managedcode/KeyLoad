using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMovePhaseResult ExecuteMoveReceiverIssue(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation, PartitionMovePhaseCommand phase, long position)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveReceiverIssueBody>(phase.Body.Span);
        var original = body.OriginalEnvelope;
        var grant = original.Grant
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority);
        if (body.Version != PartitionMoveProtocol.Version || body.ActualReceiverPrincipalId != principal.Id
            || body.ActualReceiverPolicyEpoch != principal.PolicyEpoch || !principal.ClusterAdministrator
            || phase.MoveId != original.MoveId || phase.Partition != original.Partition
            || phase.PageOrdinal != original.PageOrdinal || phase.ControlIntentDigest != original.ControlIntentDigest
            || original.ExpiresAt <= operation.EvaluatedAt || original.ReceiverIssuanceProof is not null
            || !grant.RequireReceiverIssuance || phase.GrantId is not null
            || phase.Resources.IsDefault || !phase.Resources.IsEmpty)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
        var catalog = PhysicalShardCatalogRecordSerialization.Read(transaction)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        if (PartitionMoveReceiverIssuanceStorage.Read(transaction, phase.Partition, phase.MoveId,
            body.OriginalPhaseCommandId, Limits.MaxBatchBytes) is not null)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var issued = new PartitionMoveReceiverIssuance(PartitionMoveProtocol.Version, phase.MoveId, phase.Partition,
            body.OriginalPhaseCommandId, PartitionMoveOriginalDispatchIdentity.Digest(body.OriginalPhaseCommandId, original),
            original.Nonce, original.Stage, original.PageOrdinal, grant.GrantId, grant.OperatorPrincipalId,
            grant.OperatorPolicyEpoch, principal.Id, principal.PolicyEpoch, original.ExpiresAt, catalog.DefaultShard,
            position, body.OriginalAuthorization, grant.BodyDigest, original.ControlIntentDigest,
            operation.Id, CommandFingerprint(operation));
        PartitionMoveReceiverIssuanceValidation.Require(issued, body.OriginalPhaseCommandId, original,
            body.OriginalAuthorization, catalog.DefaultShard, Limits.MaxBatchBytes);
        PartitionMoveReceiverIssuanceStorage.Write(transaction, issued, Limits.MaxBatchBytes,
            movementCheckpoints.MaxPhaseRecordsPerMove, movementCheckpoints.MaxRetainedMetadataBytesPerMove);
        return new(phase.MoveId, PartitionMovePeerStage.ReceiverIssue,
            MoveJournalReceipt(transaction, operation.Id, position, phase.ControlIntentDigest),
            null, null, null, null, ReceiverIssuance: issued);
    }
}
