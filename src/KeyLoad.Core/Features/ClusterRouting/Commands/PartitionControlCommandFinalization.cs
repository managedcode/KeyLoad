using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionControlCommandRecord FinalizeControlledDocumentCommand(IAtomicTransaction transaction,
        PrincipalRecord principal, PartitionControlFinalizeBody body)
    {
        var record = RequireControlledCommandRecord(transaction, principal, body.Identity, body.EffectId);
        if (record.Phase == PartitionControlCommandPhase.Admitted || record.OriginalResult is null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var original = record.OriginalOperation!;
        var dataPrincipal = transaction.GetRecord<PrincipalRecord>(KeySpace.Principal(original.PrincipalId))
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        if (dataPrincipal.Id != original.PrincipalId || dataPrincipal.PolicyEpoch != record.Delegation!.Principal.PolicyEpoch)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var scope = CommandOutcomePartitionIdentity.Resolve(original);
        var selected = CommandOutcomeKeyResolver.Select(transaction, original.PrincipalId, original.Id, scope);
        var stored = BuildStoredOutcome(record.Fingerprint, dataPrincipal.PolicyEpoch, record.OriginalResult,
            record.BlobAuthority, null, scope, onlineTextAuthority: null);
        if (selected.Outcome is { } previous)
        {
            if (previous.Fingerprint != record.Fingerprint
                || !NativeSerialization.Serialize(previous).AsSpan().SequenceEqual(NativeSerialization.Serialize(stored)))
            { throw Errors.Fail(ErrorCode.Conflict, CommandContentConflictMessage); }
        }
        else
        {
            transaction.PutRecord(selected.Key, stored);
            CommandOutcomePartitionLocatorSerialization.WriteScoped(transaction, scope.Partition!,
                original.PrincipalId, original.Id);
        }
        if (record.Phase == PartitionControlCommandPhase.Finalized)
        { return record; }
        var reference = new PartitionControlOutcomeReference(PartitionMoveProtocol.Version, record.Identity,
            record.Fingerprint, record.ControlOwner, selected.Key,
            Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(stored))));
        var finalized = record with { Phase = PartitionControlCommandPhase.Finalized, OriginalOutcome = reference };
        PartitionControlCommandStorage.Write(transaction, finalized, Limits.MaxBatchBytes);
        PartitionMoveGrantStorage.ChangeOutstanding(transaction, dataPrincipal.Id, false, Limits.MaxBatchMutations);
        PartitionMoveGrantStorage.ChangeOutstanding(transaction,
            PartitionMoveGrantStorage.DatabaseKey(scope.Partition!.TenantId, scope.Partition.DatabaseId),
            false, Limits.MaxBatchMutations);
        return finalized;
    }
}
