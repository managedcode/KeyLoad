using KeyLoad.Core.Features.BackupRestore.Execution;
using KeyLoad.Core.Features.CommandOutcomes.Execution;
using KeyLoad.Core.Features.CommandOutcomes.Identity;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes ResourceEffectApply operations against the caller's database context.</summary>
public static class ResourceEffectApply
{
    internal static void RequireOriginalEffectAdmission(this DatabaseEngine engine, AtomicPartitionRosterTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation) => engine.RequireResourceEffectAdmission(transaction, principal, operation);
    internal static void RequireResourceEffectAdmission(this DatabaseEngine engine, AtomicPartitionRosterTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        var admission = ResourceEffectAdmissionValidation.Read(operation, engine.Limits);
        if (admission.Error is { } error)
        {
            throw Errors.Fail(error, admission.SafeDetail!);
        }

        var observed = new ResourceEffectObservationView(transaction, transaction.EffectAdmission);
        engine.ObserveAdditionalResourceEffects(observed, principal, operation);
        if (ResourceEffectCompositionScopes.RequiresCompositionEffectCapture(operation))
        {
            transaction.EffectAdmission.ActivateOriginalBindings(observed, admission, lifetime => engine.RequireCompositionEndpointPolicy(principal, operation, lifetime));
        }
        else
        {
            transaction.EffectAdmission.Activate(admission);
        }
    }

    internal static void ValidateResourceEffectSubmission(this DatabaseEngine engine, Command<DatabaseCommand> operation, DateTimeOffset admittedAt, IResourceLifetimeWorkBinding? binding = null) => engine.ValidateResourceEffectSubmissionCore(operation, admittedAt, binding, null);
    internal static void ValidateResourceEffectSubmission(this DatabaseEngine engine, Command<DatabaseCommand> operation, DateTimeOffset admittedAt, IResourceLifetimeWorkBinding? binding, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        engine.ValidateResourceEffectSubmissionCore(operation, admittedAt, binding, work);
    }

    internal static void ValidateResourceEffectSubmissionCore(this DatabaseEngine engine, Command<DatabaseCommand> operation, DateTimeOffset admittedAt, IResourceLifetimeWorkBinding? binding, ReadExecutionBudget? work)
    {
        work?.Check();
        operation = engine.OwnCommand(operation);
        work?.Check();
        _ = engine.Store.Read(raw =>
        {
            var view = work is null ? raw : work.CreateView(raw);
            var current = new ResourceEffectBindingScope(engine.OperationLimitsOptions);
            var observed = new ResourceEffectObservationView(view, current);
            var principal = engine.Principal(observed, operation.UserId!, admittedAt);
            if (!engine.AuthorizeResourceEffectSubmission(observed, principal, operation))
            {
                return true;
            }

            var scope = CommandOutcomePartitionIdentity.Resolve(operation);
            var selected = CommandOutcomeKeyResolver.Select(observed, operation.UserId!, operation.CommandId, scope);
            if (selected.Outcome is not null)
            {
                _ = engine.ReadValidatedSubmissionOutcome(observed, operation);
                binding?.Bind(current.Capture());
                return true;
            }

            var admission = ResourceEffectAdmissionValidation.Read(operation, engine.Limits);
            if (admission.Error is { } capturedError)
            {
                if (capturedError is ErrorCode.Corruption or ErrorCode.FormatUnsupported or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome)
                {
                    throw Errors.Fail(capturedError, admission.SafeDetail!);
                }

                return true;
            }

            engine.ObserveAdditionalResourceEffects(observed, principal, operation);
            if (ResourceEffectCompositionScopes.RequiresCompositionEffectCapture(operation))
            {
                try
                {
                    current.ActivateOriginalBindings(observed, admission, lifetime => engine.RequireCompositionEndpointPolicy(principal, operation, lifetime));
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

    internal static bool AuthorizeResourceEffectSubmission(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        try
        {
            engine.AuthorizeOperation(view, principal, operation);
            return true;
        }
        catch (KeyLoadException denial) when (denial.Code == ErrorCode.PermissionDenied)
        {
            // Bound current-policy refusals retain their original canonical failed-outcome boundary.
            _ = ResourceEffectAdmissionValidation.Read(operation, engine.Limits);
            return false;
        }
    }
}
