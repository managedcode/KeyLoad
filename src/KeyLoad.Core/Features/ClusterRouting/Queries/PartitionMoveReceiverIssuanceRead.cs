using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal PartitionMoveReceiverIssuanceSnapshot ReadPartitionMovementReceiverIssuance(string principalId,
        Guid originalId, PartitionMovePeerEnvelope original, PartitionMoveJournalReceipt authorization,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant grant)
    {
        movementCheckpointVerifier.RequireReceiverAdministrator(this, principalId, work);
        var result = Store.Read(view =>
        {
            var charged = work.CreateView(view, grant);
            var principal = RequireMoveDispatchPrincipal(charged, principalId, EvaluationClock.GetUtcNow());
            var issued = PartitionMoveReceiverIssuanceStorage.Read(charged, original.Partition, original.MoveId,
                originalId, Limits.MaxBatchBytes)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
            if (principal.Id != issued.ReceiverPrincipalId)
            { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
            var catalog = PhysicalShardCatalogRecordSerialization.Read(charged)
                ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
            PhysicalShardCatalogValidation.ValidateCatalog(catalog);
            PartitionMoveReceiverIssuanceValidation.Require(issued, originalId, original, authorization,
                catalog.DefaultShard, Limits.MaxBatchBytes);
            var actual = CommandOutcomeKeyResolver.Select(charged, principal.Id, issued.IssuanceCommandId,
                new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, original.Partition)).Outcome
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
            var applied = charged.ReadOwnedValue(KeySpace.AppliedBytes)
                ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
            var cut = NativeSerialization.Deserialize<long>(applied);
            PartitionMoveReceiverIssuanceValidation.RequireStoredResult(issued, actual, Store.Identity.Incarnation, cut);
            return new PartitionMoveReceiverIssuanceSnapshot(issued, actual.Result, cut);
        });
        work.MeasureResult(result);
        work.Check();
        return result;
    }
}
