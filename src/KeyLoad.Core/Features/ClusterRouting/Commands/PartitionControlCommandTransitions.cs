using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Commands;

internal static class PartitionControlCommandTransitions
{
    internal static PartitionControlCommandRecord Admit(IAtomicTransaction transaction,
        PartitionControlCommandRecord proposed, int maximumBytes)
    {
        PartitionControlCommandValidation.Require(proposed, proposed.Identity);
        if (proposed.Phase != PartitionControlCommandPhase.Admitted)
        { throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.Invalid); }
        var previous = PartitionControlCommandStorage.Read(transaction, proposed.Identity, maximumBytes);
        if (previous is not null)
        {
            if (previous.Fingerprint != proposed.Fingerprint
                || !PhysicalOwnerEntryValidation.SameOwner(previous.ControlOwner, proposed.ControlOwner))
            { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
            return previous;
        }
        PartitionControlCommandStorage.Write(transaction, proposed, maximumBytes);
        return proposed;
    }

    internal static PartitionControlCommandRecord Acknowledge(IAtomicTransaction transaction,
        PartitionControlCommandIdentity identity, Guid effectId, CommitReceipt receipt,
        OperationResult originalResult, int maximumBytes)
    {
        var previous = RequireRetained(transaction, identity, effectId, maximumBytes);
        var payload = new PartitionControlEffectPayload(receipt, originalResult);
        if (NativeSerialization.Measure(payload) > maximumBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        var digest = Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(payload)));
        if (previous.Phase != PartitionControlCommandPhase.Admitted)
        {
            if (previous.TargetEffectDigest != digest)
            { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
            return previous;
        }
        var acknowledged = previous with
        {
            Phase = PartitionControlCommandPhase.EffectAcknowledged,
            TargetEffect = receipt,
            TargetEffectDigest = digest,
            OriginalResult = originalResult
        };
        PartitionControlCommandStorage.Write(transaction, acknowledged, maximumBytes);
        return acknowledged;
    }

    private static PartitionControlCommandRecord RequireRetained(IKeyValueView view,
        PartitionControlCommandIdentity identity, Guid effectId, int maximumBytes)
    {
        var previous = PartitionControlCommandStorage.Read(view, identity, maximumBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (previous.EffectId != effectId)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        return previous;
    }
}
