using System.Collections.Immutable;
using KeyLoad.Core.DatabaseComposition;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes ResourceEffectCapture operations against the caller's database context.</summary>
public static class ResourceEffectCapture
{
    internal static byte[] CaptureResourceEffectAdmission(this DatabaseEngine engine, Command<DatabaseCommand> operation)
    {
        var captured = ResourceEffectCompositionScopes.RequiresCompositionEffectCapture(operation) ? engine.Store.Prepare((transaction, position) => engine.CaptureResourceEffectAdmission(transaction, operation, preparationPosition: position)) : engine.Store.Read(raw => engine.CaptureResourceEffectAdmission(raw, operation));
        return ResourceEffectCapture.EncodeResourceEffectAdmission(captured);
    }

    internal static byte[] CaptureResourceEffectAdmission(this DatabaseEngine engine, Command<DatabaseCommand> operation, ReadExecutionBudget work)
    {
        work.Check();
        var captured = ResourceEffectCompositionScopes.RequiresCompositionEffectCapture(operation) ? engine.Store.Prepare((transaction, position) => engine.CaptureResourceEffectAdmission(transaction, operation, preserveBudgetFailure: true, preparationPosition: position, preparationWork: work)) : engine.Store.Read(raw => engine.CaptureResourceEffectAdmission(work.CreateView(raw), operation, preserveBudgetFailure: true));
        work.Check();
        return ResourceEffectCapture.EncodeResourceEffectAdmission(captured);
    }

    internal static ResourceEffectAdmission CaptureResourceEffectAdmission(this DatabaseEngine engine, IKeyValueView raw, Command<DatabaseCommand> operation, bool preserveBudgetFailure = false, long? preparationPosition = null, ReadExecutionBudget? preparationWork = null)
    {
        var scope = new ResourceEffectBindingScope();
        var preparation = preparationPosition is { } position ? new ResourceEffectPreparationTransaction(new KeyLoad.Core.BackupRestore.Execution.AtomicPartitionRosterTransaction((IAtomicTransaction)raw, position, KeyLoad.Core.BackupRestore.Serialization.AtomicPartitionRosterProtocol.NoCanonicalSequence), scope, preparationWork ?? new ReadExecutionBudget(CompositionTimeProvider.Instance)) : null;
        var view = preparation is null ? new ResourceEffectObservationView(raw, scope) : preparation;
        try
        {
            var principal = engine.Principal(view, operation.UserId!, engine.Clock.GetUtcNow());
            _ = (engine.AuthorizeOperation(view, principal, operation)).Value;
            engine.ObserveAdditionalResourceEffects(view, principal, operation);
            if (preparation is not null)
            {
                engine.ObserveCompositionPrefix(preparation, principal, operation, preparationPosition!.Value, preserveBudgetFailure);
            }

            var admission = new ResourceEffectAdmission(ResourceEffectAdmissionProtocol.Version, scope.Capture(), null, null);
            scope.Activate(admission);
            return admission;
        }
        catch (KeyLoadException error) when (!preserveBudgetFailure || error.Code != ErrorCode.BudgetExceeded)
        {
            return new(ResourceEffectAdmissionProtocol.Version, ImmutableArray<ResourceLifetime>.Empty, error.Code, error.Message);
        }
    }

    internal static byte[] EncodeResourceEffectAdmission(ResourceEffectAdmission admission)
    {
        var bytes = OrleansSerialization.Serialize(admission);
        return bytes;
    }
}
