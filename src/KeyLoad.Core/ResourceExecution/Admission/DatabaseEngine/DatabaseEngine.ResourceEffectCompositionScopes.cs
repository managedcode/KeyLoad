using ManagedCode.Communication.Commands;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static bool RequiresCompositionEffectCapture(Command<DatabaseCommand> operation)
    {
        if (operation.Value!.Kind == OperationKind.Batch)
        {
            var batch = Payload<CommandRequest>(operation);
            return !batch.Mutations.IsDefault && batch.Mutations.Any(static mutation
                => mutation is QueueToGraph or GraphToQueueMutation or UpsertEdge or DeleteEdge);
        }
        return false;
    }

    private static void RequireCompleteCompositionEffects(IAtomicTransaction transaction,
        Command<DatabaseCommand>? operation, bool preparingComposition)
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

    private void RequireCompositionEndpointPolicy(PrincipalRecord principal, Command<DatabaseCommand> operation,
        ResourceLifetimeV1 lifetime)
    {
        var partition = Payload<CommandRequest>(operation).Partition;
        if (lifetime.TenantId != partition.TenantId || lifetime.DatabaseId != partition.DatabaseId
            || lifetime.TransactionDomainId != partition.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceEffectAdmissionProtocol.Invalid);
        }
        Authorization.Require(principal, partition, lifetime.ResourceName, Capability.DocumentsRead);
    }

    private void ObserveCompositionPrefix(ResourceEffectPreparationTransaction transaction,
        PrincipalRecord principal, Command<DatabaseCommand> operation, long position, bool preserveBudgetFailure)
    {
        var batch = Payload<CommandRequest>(operation);
        try
        {
            // This is the real provider's discard-only child. The ordinary ordered handlers see
            // every earlier staged effect; no source selection or receipt is retained for real apply.
            _ = ApplyMutations(transaction, principal, batch.Partition, batch.Mutations, operation.Timestamp,
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
