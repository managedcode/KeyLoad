using KeyLoad.Core.Features.BackupRestore.Execution;
using KeyLoad.Core.Features.CommandOutcomes.Execution;
using KeyLoad.Core.Features.RuntimeJournaling.Execution;
using KeyLoad.Core.Features.CommandOutcomes.Identity;
using KeyLoad.Core.Features.RuntimeJournaling.Identity;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireOriginalEffectAdmission(AtomicPartitionRosterTransaction transaction,
        PrincipalRecord principal, Command<DatabaseCommand> operation)
        => RequireResourceEffectAdmission(transaction, principal, operation);

    private void RequireResourceEffectAdmission(AtomicPartitionRosterTransaction transaction,
        PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        var admission = ResourceEffectAdmissionValidation.Read(operation, Limits);
        if (admission.Error is { } error)
        {
            throw Errors.Fail(error, admission.SafeDetail!);
        }
        var observed = new ResourceEffectObservationView(transaction, transaction.EffectAdmission);
        ObserveAdditionalResourceEffects(observed, principal, operation);
        if (RequiresCompositionEffectCapture(operation))
        {
            transaction.EffectAdmission.ActivateOriginalBindings(observed, admission,
                lifetime => RequireCompositionEndpointPolicy(principal, operation, lifetime));
        }
        else
        {
            transaction.EffectAdmission.Activate(admission);
        }
    }

    internal void ValidateResourceEffectSubmission(Command<DatabaseCommand> operation, DateTimeOffset admittedAt,
        IResourceLifetimeWorkBinding? binding = null)
        => ValidateResourceEffectSubmissionCore(operation, admittedAt, binding, null);

    internal void ValidateResourceEffectSubmission(Command<DatabaseCommand> operation, DateTimeOffset admittedAt,
        IResourceLifetimeWorkBinding? binding, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        ValidateResourceEffectSubmissionCore(operation, admittedAt, binding, work);
    }

    private void ValidateResourceEffectSubmissionCore(Command<DatabaseCommand> operation, DateTimeOffset admittedAt,
        IResourceLifetimeWorkBinding? binding, ReadExecutionBudget? work)
    {
        work?.Check();
        operation = OwnCommand(operation);
        work?.Check();
        _ = Store.Read(raw =>
        {
            var view = work is null ? raw : work.CreateView(raw);
            var current = new ResourceEffectBindingScope(OperationLimitsOptions);
            var observed = new ResourceEffectObservationView(view, current);
            var principal = Principal(observed, operation.UserId!, admittedAt);
            if (!AuthorizeResourceEffectSubmission(observed, principal, operation))
            {
                return true;
            }
            var scope = CommandOutcomePartitionIdentity.Resolve(operation);
            var selected = CommandOutcomeKeyResolver.Select(observed, operation.UserId!, operation.CommandId, scope);
            if (selected.Outcome is not null)
            {
                _ = ReadValidatedSubmissionOutcome(observed, operation);
                binding?.Bind(current.Capture());
                return true;
            }
            var admission = ResourceEffectAdmissionValidation.Read(operation, Limits);
            if (admission.Error is { } capturedError)
            {
                if (capturedError is ErrorCode.Corruption or ErrorCode.FormatUnsupported
                    or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome)
                {
                    throw Errors.Fail(capturedError, admission.SafeDetail!);
                }
                return true;
            }
            ObserveAdditionalResourceEffects(observed, principal, operation);
            if (RequiresCompositionEffectCapture(operation))
            {
                try
                {
                    current.ActivateOriginalBindings(observed, admission,
                        lifetime => RequireCompositionEndpointPolicy(principal, operation, lifetime));
                }
                catch (KeyLoadException denial) when (denial.Code == ErrorCode.PermissionDenied)
                {
                    // Match static current-policy refusals: the actual command retains its
                    // canonical PermissionDenied outcome without revealing private lifetime state.
                    return true;
                }
                binding?.Bind(admission.Bindings);
            }
            else
            {
                current.Activate(admission);
                binding?.Bind(current.Capture());
            }
            return true;
        });
        work?.Check();
    }

    private bool AuthorizeResourceEffectSubmission(IKeyValueView view, PrincipalRecord principal,
        Command<DatabaseCommand> operation)
    {
        try
        {
            AuthorizeOperation(view, principal, operation);
            return true;
        }
        catch (KeyLoadException denial) when (denial.Code == ErrorCode.PermissionDenied)
        {
            // Bound current-policy refusals retain their original canonical failed-outcome boundary.
            _ = ResourceEffectAdmissionValidation.Read(operation, Limits);
            return false;
        }
    }
}
