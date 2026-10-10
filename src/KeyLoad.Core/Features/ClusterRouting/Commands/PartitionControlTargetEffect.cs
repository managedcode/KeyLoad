using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionControlEffectPayload ApplyControlledDocumentEffect(IAtomicTransaction transaction,
        PartitionMovePhaseCommand phase, Guid effectId, long appliedPosition, DateTimeOffset evaluatedAt)
    {
        var body = NativeSerialization.Deserialize<PartitionControlApplyBody>(phase.Body.Span);
        var placement = ReadPlacementWitness(transaction, phase.Partition);
        OperationResult originalResult;
        BlobOutcomeAuthority? blobAuthority = null;
        try
        {
            var admitted = RequireControlledDocumentTarget(transaction, phase, body, effectId, evaluatedAt);
            var controlled = new PartitionControlEffectTransaction(transaction, phase.Partition,
                body.Delegation.Resources, Limits.MaxBatchBytes);
            var original = admitted.Original with { EvaluatedAt = evaluatedAt };
            if (admitted.Command is { } command)
            {
                var authorized = AuthorizeBatch(controlled, body.Delegation.Principal, command);
                originalResult = ExecuteBatch(controlled, body.Delegation.Principal, original,
                    appliedPosition, authorized);
            }
            else
            {
                var blobs = new BlobStorageOperations(this);
                blobs.Authorize(controlled, body.Delegation.Principal, original);
                blobAuthority = blobs.CaptureOutcomeAuthority(controlled, body.Delegation.Principal, original);
                originalResult = blobs.Execute(controlled, body.Delegation.Principal, original, appliedPosition);
            }
        }
        catch (KeyLoadException error) when (error.Code is not (ErrorCode.Corruption or ErrorCode.FormatUnsupported
            or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome))
        {
            transaction.Reset();
            blobAuthority = null;
            originalResult = new(null, error.Code, error.Message);
        }
        var mutations = originalResult.Error is null && body.Delegation.OriginalKind == OperationKind.Batch
            ? originalResult.Get<CommitReceipt>().Mutations
            : System.Collections.Immutable.ImmutableArray<MutationReceipt>.Empty;
        var token = new CommitToken(placement.Incarnation, phase.Partition.AtomicPartitionId,
            appliedPosition, placement.PlacementEpoch);
        var effect = new CommitReceipt(effectId, token, mutations, Durability);
        return new(effect, originalResult, blobAuthority);
    }
}
