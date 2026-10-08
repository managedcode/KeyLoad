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
        try
        {
            var admitted = RequireControlledDocumentTarget(transaction, phase, body, effectId, evaluatedAt);
            var controlled = new PartitionControlEffectTransaction(transaction, phase.Partition,
                body.Delegation.Resources, Limits.MaxBatchBytes);
            var original = admitted.Original with { EvaluatedAt = evaluatedAt };
            var authorized = AuthorizeBatch(controlled, body.Delegation.Principal, admitted.Command);
            originalResult = ExecuteBatch(controlled, body.Delegation.Principal, original,
                appliedPosition, authorized);
        }
        catch (KeyLoadException error) when (error.Code is not (ErrorCode.Corruption or ErrorCode.FormatUnsupported
            or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome))
        {
            transaction.Reset();
            originalResult = new(null, error.Code, error.Message);
        }
        var mutations = originalResult.Error is null
            ? originalResult.Get<CommitReceipt>().Mutations
            : System.Collections.Immutable.ImmutableArray<MutationReceipt>.Empty;
        var token = new CommitToken(placement.Incarnation, phase.Partition.AtomicPartitionId,
            appliedPosition, placement.PlacementEpoch);
        var effect = new CommitReceipt(effectId, token, mutations, Durability);
        return new(effect, originalResult);
    }
}
