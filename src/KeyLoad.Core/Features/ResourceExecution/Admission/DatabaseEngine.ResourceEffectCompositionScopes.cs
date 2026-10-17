using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static bool RequiresCompositionEffectCapture(ReplicatedOperation operation)
    {
        if (operation.Kind == OperationKind.Batch)
        {
            var batch = Payload<CommandRequest>(operation);
            return !batch.Mutations.IsDefault && batch.Mutations.Any(static mutation
                => mutation is QueueToGraph or GraphToQueueMutation or UpsertEdge or DeleteEdge);
        }
        if (operation.Kind == OperationKind.PartitionMovementPhase)
        {
            var phase = PartitionMovementNativePayload.ReadAuthenticated(operation).Original;
            if (phase.Stage == PartitionMovePeerStage.ControlApplyCommand)
            {
                var body = NativeSerialization.Deserialize<PartitionControlApplyBody>(phase.Body.Span);
                return body.Delegation.OriginalOperation is { Kind: OperationKind.Batch } original
                    && RequiresCompositionEffectCapture(original);
            }
        }
        return false;
    }

    private static void RequireCompleteCompositionEffects(IAtomicTransaction transaction,
        ReplicatedOperation? operation, bool preparingComposition)
    {
        if (preparingComposition || operation is null || !RequiresCompositionEffectCapture(operation))
        {
            return;
        }
        if (transaction is not IResourceEffectCompletion completion)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, ResourceEffectAdmissionProtocol.Unbound);
        }
        completion.RequireCompleteResourceEffects();
    }

    private void RequireCompositionEndpointPolicy(PrincipalRecord principal, ReplicatedOperation operation,
        ResourceLifetimeV1 lifetime)
    {
        PartitionRef partition;
        if (operation.Kind == OperationKind.PartitionMovementPhase)
        {
            var phase = PartitionMovementNativePayload.ReadAuthenticated(operation).Original;
            var body = NativeSerialization.Deserialize<PartitionControlApplyBody>(phase.Body.Span);
            principal = body.Delegation.Principal;
            partition = phase.Partition;
        }
        else
        {
            partition = Payload<CommandRequest>(operation).Partition;
        }
        if (lifetime.TenantId != partition.TenantId || lifetime.DatabaseId != partition.DatabaseId
            || lifetime.TransactionDomainId != partition.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceEffectAdmissionProtocol.Invalid);
        }
        Authorization.Require(principal, partition, lifetime.ResourceName, Capability.DocumentsRead);
    }

    private IKeyValueView CompositionAdmissionView(IKeyValueView view, ReplicatedOperation operation)
    {
        if (operation.Kind != OperationKind.PartitionMovementPhase)
        {
            return view;
        }
        var authenticated = PartitionMovementNativePayload.ReadAuthenticated(operation);
        var phase = authenticated.Original;
        var proof = authenticated.LifetimeProofs.Single();
        var coverage = PartitionControlLifetimeProofValidation.Require(phase, proof, Limits.MaxBatchBytes);
        var body = NativeSerialization.Deserialize<PartitionControlApplyBody>(phase.Body.Span);
        return new PartitionControlResourceView(view, phase.Partition, body.Delegation.Resources,
            coverage, Limits.MaxBatchBytes);
    }

    private void ObserveCompositionPrefix(ResourceEffectPreparationTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation, long position, bool preserveBudgetFailure)
    {
        if (operation.Kind == OperationKind.PartitionMovementPhase)
        {
            var authenticated = PartitionMovementNativePayload.ReadAuthenticated(operation);
            var phase = authenticated.Original;
            var proof = authenticated.LifetimeProofs.Single();
            var coverage = PartitionControlLifetimeProofValidation.Require(phase, proof, Limits.MaxBatchBytes);
            var body = NativeSerialization.Deserialize<PartitionControlApplyBody>(phase.Body.Span);
            var original = body.Delegation.OriginalOperation
                ?? throw Errors.Fail(ErrorCode.Corruption, ResourceEffectAdmissionProtocol.Invalid);
            operation = original with
            {
                EvaluatedAt = operation.EvaluatedAt
            };
            principal = body.Delegation.Principal;
            transaction = transaction.WithControlledResources(phase.Partition, body.Delegation.Resources,
                coverage, Limits.MaxBatchBytes);
        }
        var batch = Payload<CommandRequest>(operation);
        try
        {
            // This is the real provider's discard-only child. The ordinary ordered handlers see
            // every earlier staged effect; no source selection or receipt is retained for real apply.
            _ = ApplyMutations(transaction, principal, batch.Partition, batch.Mutations, operation.EvaluatedAt,
                position, operation: operation, preparingComposition: true, compositionBudget: transaction.Work);
        }
        catch (KeyLoadException error) when (error.Code is not (ErrorCode.Corruption or ErrorCode.FormatUnsupported
            or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome)
            && (!preserveBudgetFailure || error.Code != ErrorCode.BudgetExceeded || error.Message == CompositionLimit))
        {
            // A prefix business failure is executed again at the original business point after
            // the real clock gate. Only lifetimes actually observed before it can be signed.
            // No rejected staged data leaves this provider-owned child.
        }
    }
}
