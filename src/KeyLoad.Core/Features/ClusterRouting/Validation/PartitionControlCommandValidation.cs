using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PartitionControlCommandValidation
{
    internal static void Require(PartitionControlCommandRecord record, PartitionControlCommandIdentity identity)
    {
        if (record is null || identity is null || record.Version != PartitionMoveProtocol.Version
            || record.Identity != identity || record.ControlOwner is null || record.Destination is null
            || record.ControlOwner.Incarnation == Guid.Empty || record.EffectId == Guid.Empty
            || record.AdmissionPosition <= PartitionMoveProtocol.EmptyCount
            || !Enum.IsDefined(record.Phase)
            || !PartitionMoveSourceFenceValidation.ValidDigest(record.Fingerprint)
            || record.Destination.Partition != identity.Partition
            || record.Destination.Incarnation == Guid.Empty
            || record.Destination.PlacementEpoch <= PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        _ = PartitionControlCommandKeys.Original(identity);
        if (record.Phase == PartitionControlCommandPhase.Admitted)
        {
            if (record.TargetEffect is not null || record.TargetEffectDigest is not null
                || record.OriginalOutcome is not null || record.OriginalResult is not null)
            { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
            return;
        }
        RequireEffect(record);
        if (record.Phase == PartitionControlCommandPhase.Finalized
            && (record.OriginalOutcome is null || record.OriginalOutcome.Identity != identity
                || record.OriginalOutcome.Fingerprint != record.Fingerprint))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
    }

    private static void RequireEffect(PartitionControlCommandRecord record)
    {
        var effect = record.TargetEffect;
        if (effect is null || effect.Token is null || record.OriginalResult is null
            || effect.CommandId != record.EffectId || effect.Token.Incarnation != record.Destination.Incarnation
            || effect.Token.AtomicPartitionId != record.Destination.Partition.AtomicPartitionId
            || effect.Token.OwnershipEpoch != record.Destination.PlacementEpoch
            || effect.Token.Position <= PartitionMoveProtocol.EmptyCount
            || record.TargetEffectDigest is not { } effectDigest
            || !PartitionMoveSourceFenceValidation.ValidDigest(effectDigest))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        var digest = SHA256.HashData(NativeSerialization.Serialize(new PartitionControlEffectPayload(effect, record.OriginalResult)));
        if (!CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(effectDigest)))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
    }
}
