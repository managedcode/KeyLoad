using System.Collections.Immutable;
using KeyLoad.Core.Features.DatabaseComposition;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private byte[] CaptureResourceEffectAdmission(Command<DatabaseCommand> operation)
    {
        var fingerprint = operation.Value!.Fingerprint;
        var captured = RequiresCompositionEffectCapture(operation)
            ? Store.Prepare((transaction, position) => CaptureResourceEffectAdmission(transaction, operation,
                fingerprint, preparationPosition: position))
            : Store.Read(raw => CaptureResourceEffectAdmission(raw, operation, fingerprint));
        return EncodeResourceEffectAdmission(captured);
    }

    private byte[] CaptureResourceEffectAdmission(Command<DatabaseCommand> operation, ReadExecutionBudget work)
    {
        work.Check();
        var fingerprint = operation.Value!.Fingerprint;
        var captured = RequiresCompositionEffectCapture(operation)
            ? Store.Prepare((transaction, position) => CaptureResourceEffectAdmission(transaction, operation,
                fingerprint, preserveBudgetFailure: true, preparationPosition: position, preparationWork: work))
            : Store.Read(raw => CaptureResourceEffectAdmission(work.CreateView(raw), operation,
                fingerprint, preserveBudgetFailure: true));
        work.Check();
        return EncodeResourceEffectAdmission(captured);
    }

    private ResourceEffectAdmissionV1 CaptureResourceEffectAdmission(IKeyValueView raw,
        Command<DatabaseCommand> operation, string fingerprint, bool preserveBudgetFailure = false,
        long? preparationPosition = null, ReadExecutionBudget? preparationWork = null)
    {
        var scope = new ResourceEffectBindingScope(OperationLimitsOptions);
        var preparation = preparationPosition is { } position
            ? new ResourceEffectPreparationTransaction(
                new KeyLoad.Core.Features.BackupRestore.Execution.AtomicPartitionRosterTransaction(
                    (IAtomicTransaction)raw, OperationLimitsOptions, position,
                    KeyLoad.Core.Features.BackupRestore.Serialization.AtomicPartitionRosterProtocol.NoCanonicalSequence), scope,
                preparationWork ?? new ReadExecutionBudget(OperationLimitsOptions, CompositionTimeProvider.Instance))
            : null;
        var view = preparation is null
            ? new ResourceEffectObservationView(raw, scope) : preparation;
        try
        {
            var principal = Principal(view, operation.UserId!, Clock.GetUtcNow());
            AuthorizeOperation(view, principal, operation);
            ObserveAdditionalResourceEffects(view, principal, operation);
            if (preparation is not null)
            {
                ObserveCompositionPrefix(preparation, principal, operation, preparationPosition!.Value,
                    preserveBudgetFailure);
            }
            var admission = new ResourceEffectAdmissionV1(ResourceEffectAdmissionProtocol.Version,
                fingerprint, scope.Capture(), null, null);
            scope.Activate(admission);
            return admission;
        }
        catch (KeyLoadException error) when (!preserveBudgetFailure || error.Code != ErrorCode.BudgetExceeded)
        {
            return new(ResourceEffectAdmissionProtocol.Version, fingerprint,
                ImmutableArray<ResourceLifetimeV1>.Empty,
                error.Code, error.Message);
        }
    }

    private byte[] EncodeResourceEffectAdmission(ResourceEffectAdmissionV1 admission)
    {
        var bytes = NativeSerialization.Serialize(admission);
        RequireNativeBudget(bytes.Length);
        return bytes;
    }
}
