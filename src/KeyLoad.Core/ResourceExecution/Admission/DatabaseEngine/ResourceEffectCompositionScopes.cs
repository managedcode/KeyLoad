using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes ResourceEffectCompositionScopes operations against the caller's database context.</summary>
public static class ResourceEffectCompositionScopes
{
    internal static bool RequiresCompositionEffectCapture(Command<DatabaseCommand> operation)
    {
        if (operation.Value!.Kind == OperationKind.Batch)
        {
            var batch = OperationProtocol.Payload<CommandRequest>(operation);
            return !batch.Mutations.IsDefault && batch.Mutations.Any(static mutation => mutation is QueueToGraph or GraphToQueueMutation or UpsertEdge or DeleteEdge);
        }

        return false;
    }

    internal static void RequireCompleteCompositionEffects(IAtomicTransaction transaction, Command<DatabaseCommand>? operation, bool preparingComposition)
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

    internal static void RequireCompositionEndpointPolicy(this DatabaseEngine engine, PrincipalRecord principal, Command<DatabaseCommand> operation, ResourceLifetime lifetime)
    {
        var partition = OperationProtocol.Payload<CommandRequest>(operation).Partition;
        if (lifetime.TenantId != partition.TenantId || lifetime.DatabaseId != partition.DatabaseId || lifetime.TransactionDomainId != partition.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceEffectAdmissionProtocol.Invalid);
        }

        engine.Authorization.Require(principal, partition, lifetime.ResourceName, Capability.DocumentsRead);
    }

    internal static void ObserveCompositionPrefix(this DatabaseEngine engine, ResourceEffectPreparationTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation, long position, bool preserveBudgetFailure)
    {
        var batch = OperationProtocol.Payload<CommandRequest>(operation);
        try
        {
            // This is the real provider's discard-only child. The ordinary ordered handlers see
            // every earlier staged effect; no source selection or receipt is retained for real apply.
            _ = engine.ApplyMutations(transaction, principal, batch.Partition, batch.Mutations, operation.Timestamp, position, operation: operation, preparingComposition: true, compositionBudget: transaction.Work);
        }
        catch (KeyLoadException error) when (error.Code is not (ErrorCode.Corruption or ErrorCode.FormatUnsupported or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome) && (!preserveBudgetFailure || error.Code != ErrorCode.BudgetExceeded || error.Message == DatabaseCompositionExecution.CompositionLimit))
        {
            // A prefix business failure is executed again at the original business point after
            // the real clock gate. Only lifetimes actually observed before it can be signed.
            // No rejected staged data leaves this provider-owned child.
        }
    }
}
