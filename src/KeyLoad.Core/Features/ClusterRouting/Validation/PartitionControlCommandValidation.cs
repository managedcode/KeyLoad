using KeyLoad.Core.Features.BlobStorage;
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
            || record.Phase == PartitionControlCommandPhase.None || !Enum.IsDefined(record.Phase)
            || !PartitionMoveSourceFenceValidation.ValidDigest(record.Fingerprint)
            || record.Destination.Partition != identity.Partition
            || record.Destination.Incarnation == Guid.Empty
            || record.Destination.PlacementEpoch <= PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        _ = PartitionControlCommandKeys.Original(identity);
        if (identity.ScopeKind != CommandOutcomeScopeKind.Partition || identity.Partition is null
            || record.Delegation is null || record.OriginalOperation is null
            || record.Delegation.Identity != identity || record.Delegation.EffectId != record.EffectId
            || record.Delegation.Fingerprint != record.Fingerprint
            || record.OriginalOperation.Id != identity.CommandId
            || record.OriginalOperation.PrincipalId != identity.PrincipalId
            || record.OriginalOperation.Kind != OperationKind.Batch
                && !BlobStorageOperations.Handles(record.OriginalOperation.Kind))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }

        PartitionControlTargetBodyValidation.Require(record);
        if (record.Phase == PartitionControlCommandPhase.Admitted)
        {
            if (record.TargetEffect is not null || record.TargetEffectDigest is not null
                || record.OriginalOutcome is not null || record.OriginalResult is not null || record.BlobAuthority is not null)
            { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
            return;
        }
        RequireBlobAuthority(record);
        RequireEffect(record);
        if (record.Phase == PartitionControlCommandPhase.Finalized
            && (record.OriginalOutcome is null || record.OriginalOutcome.Identity != identity
                || record.OriginalOutcome.Fingerprint != record.Fingerprint
                || record.OriginalOutcome.Version != PartitionMoveProtocol.Version
                || !record.OriginalOutcome.OutcomeKey.Span.SequenceEqual(PartitionControlCommandKeys.Original(identity))
                || !PartitionMoveSourceFenceValidation.ValidDigest(record.OriginalOutcome.OutcomeDigest)))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
    }

    private static void RequireBlobAuthority(PartitionControlCommandRecord record)
    {
        if (record.BlobAuthority is not { } authority)
        {
            if (record.OriginalResult?.Error is null && record.OriginalOperation!.Kind is
                OperationKind.BeginBlobUpload or OperationKind.WriteBlobPart or OperationKind.CompleteBlobUpload or OperationKind.AbortBlobUpload)
            { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
            return;
        }
        if (!BlobStorageOperations.Handles(record.OriginalOperation!.Kind) || record.OriginalResult?.Error is not null
            || authority.FormatVersion != BlobKeys.FormatVersion || authority.BeginCommandId == Guid.Empty
            || string.IsNullOrEmpty(authority.CreatorPrincipalId))
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
        var digest = SHA256.HashData(NativeSerialization.Serialize(new PartitionControlEffectPayload(effect, record.OriginalResult, record.BlobAuthority)));
        if (!CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(effectDigest)))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
    }
}
