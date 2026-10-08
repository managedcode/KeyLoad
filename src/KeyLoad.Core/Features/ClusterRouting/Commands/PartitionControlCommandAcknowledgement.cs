using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionControlCommandRecord AcknowledgeControlledDocumentCommand(IAtomicTransaction transaction,
        PrincipalRecord principal, PartitionControlAcknowledgeBody body)
    {
        var record = RequireControlledCommandRecord(transaction, principal, body.Identity, body.EffectId);
        var grant = PartitionMoveGrantStorage.Read(transaction, body.Identity.Partition!, body.TargetGrantId,
            Limits.MaxBatchBytes) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var bodyDigest = Convert.ToHexStringLower(SHA256.HashData(record.TargetBody.Span));
        var payload = new PartitionControlEffectPayload(body.TargetReceipt, body.OriginalResult);
        var digest = Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(payload)));
        PartitionControlCommandValidation.Require(record with
        {
            Phase = PartitionControlCommandPhase.EffectAcknowledged,
            TargetEffect = body.TargetReceipt,
            TargetEffectDigest = digest,
            OriginalResult = body.OriginalResult
        }, body.Identity);
        if (grant.BodyDigest != bodyDigest || grant.Stage != PartitionMovePeerStage.ControlApplyCommand || grant.PhaseCommandId != record.EffectId
            || grant.MoveId != record.Delegation!.MoveId || grant.AbortDisposition is not null
            || grant.Settlement is not { } settlement || settlement.CommandId != record.EffectId
            || settlement.EffectDigest != digest || settlement.AppliedPosition != body.TargetReceipt.Token.Position
            || settlement.PhysicalOwner.Incarnation != record.Destination.Incarnation
            || settlement.PhysicalOwner.PhysicalShardId != record.Destination.PhysicalShardId)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        if (record.Phase != PartitionControlCommandPhase.Admitted)
        {
            if (record.TargetEffectDigest != digest)
            { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
            return record;
        }
        var acknowledged = record with
        {
            Phase = PartitionControlCommandPhase.EffectAcknowledged,
            TargetEffect = body.TargetReceipt,
            TargetEffectDigest = digest,
            OriginalResult = body.OriginalResult
        };
        PartitionControlCommandStorage.Write(transaction, acknowledged, Limits.MaxBatchBytes);
        return acknowledged;
    }

    private PartitionControlCommandRecord RequireControlledCommandRecord(IKeyValueView view,
        PrincipalRecord principal, PartitionControlCommandIdentity identity, Guid effectId)
    {
        var record = PartitionControlCommandStorage.Read(view, identity, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (!principal.ClusterAdministrator || record.EffectId != effectId || record.Delegation is null
            || record.OriginalOperation is null || record.Delegation.ControlOwner is null
            || record.ControlOwner.Incarnation != Store.Identity.Incarnation)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var control = PartitionMoveControlStorage.ReadHistory(view, identity.Partition!, record.Delegation.MoveId,
            Limits.MaxBatchBytes) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        _ = RequireRetiredCommandControl(view, principal, control, out _);
        return record;
    }
}
