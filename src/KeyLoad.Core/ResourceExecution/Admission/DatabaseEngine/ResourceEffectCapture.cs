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
        var fingerprint = operation.Value!.Fingerprint;
        var captured = ResourceEffectCompositionScopes.RequiresCompositionEffectCapture(operation) ? engine.Store.Prepare((transaction, position) => engine.CaptureResourceEffectAdmission(transaction, operation, fingerprint, preparationPosition: position)) : engine.Store.Read(raw => engine.CaptureResourceEffectAdmission(raw, operation, fingerprint));
        return engine.EncodeResourceEffectAdmission(captured);
    }

    internal static byte[] CaptureResourceEffectAdmission(this DatabaseEngine engine, Command<DatabaseCommand> operation, ReadExecutionBudget work)
    {
        work.Check();
        var fingerprint = operation.Value!.Fingerprint;
        var captured = ResourceEffectCompositionScopes.RequiresCompositionEffectCapture(operation) ? engine.Store.Prepare((transaction, position) => engine.CaptureResourceEffectAdmission(transaction, operation, fingerprint, preserveBudgetFailure: true, preparationPosition: position, preparationWork: work)) : engine.Store.Read(raw => engine.CaptureResourceEffectAdmission(work.CreateView(raw), operation, fingerprint, preserveBudgetFailure: true));
        work.Check();
        return engine.EncodeResourceEffectAdmission(captured);
    }

    internal static ResourceEffectAdmission CaptureResourceEffectAdmission(this DatabaseEngine engine, IKeyValueView raw, Command<DatabaseCommand> operation, string fingerprint, bool preserveBudgetFailure = false, long? preparationPosition = null, ReadExecutionBudget? preparationWork = null)
    {
        var scope = new ResourceEffectBindingScope(engine.OperationLimitsOptions);
        var preparation = preparationPosition is { } position ? new ResourceEffectPreparationTransaction(new KeyLoad.Core.BackupRestore.Execution.AtomicPartitionRosterTransaction((IAtomicTransaction)raw, engine.OperationLimitsOptions, position, KeyLoad.Core.BackupRestore.Serialization.AtomicPartitionRosterProtocol.NoCanonicalSequence), scope, preparationWork ?? new ReadExecutionBudget(engine.OperationLimitsOptions, CompositionTimeProvider.Instance)) : null;
        var view = preparation is null ? new ResourceEffectObservationView(raw, scope) : preparation;
        try
        {
            var principal = engine.Principal(view, operation.UserId!, engine.Clock.GetUtcNow());
            engine.AuthorizeOperation(view, principal, operation);
            engine.ObserveAdditionalResourceEffects(view, principal, operation);
            if (preparation is not null)
            {
                engine.ObserveCompositionPrefix(preparation, principal, operation, preparationPosition!.Value, preserveBudgetFailure);
            }

            var admission = new ResourceEffectAdmission(ResourceEffectAdmissionProtocol.Version, fingerprint, scope.Capture(), null, null);
            scope.Activate(admission);
            return admission;
        }
        catch (KeyLoadException error) when (!preserveBudgetFailure || error.Code != ErrorCode.BudgetExceeded)
        {
            return new(ResourceEffectAdmissionProtocol.Version, fingerprint, ImmutableArray<ResourceLifetime>.Empty, error.Code, error.Message);
        }
    }

    internal static byte[] EncodeResourceEffectAdmission(this DatabaseEngine engine, ResourceEffectAdmission admission)
    {
        var bytes = OrleansSerialization.Serialize(admission);
        engine.RequireBudget(bytes.Length);
        return bytes;
    }
}
