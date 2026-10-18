using KeyLoad.Core.Features.BackupRestore.Execution;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireOriginalEffectAdmission(AtomicPartitionRosterTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation)
        => RequireResourceEffectAdmission(transaction, principal, operation);

    private void RequireResourceEffectAdmission(AtomicPartitionRosterTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation)
    {
        var admission = ResourceEffectAdmissionValidation.Read(operation, Limits);
        if (admission.Error is { } error)
        {
            var payload = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
            if (payload.Error == error && payload.SafeDetail == admission.SafeDetail)
            {
                // The authenticated decode marker belongs to the original domain call after the clock check.
                return;
            }
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
            transaction.EffectAdmission.Activate(observed, admission);
        }
    }

    internal void ValidateResourceEffectSubmission(ReplicatedOperation operation, DateTimeOffset admittedAt,
        IResourceLifetimeWorkBinding? binding = null)
        => ValidateResourceEffectSubmissionCore(operation, admittedAt, binding, null);

    internal void ValidateResourceEffectSubmission(ReplicatedOperation operation, DateTimeOffset admittedAt,
        IResourceLifetimeWorkBinding? binding, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        ValidateResourceEffectSubmissionCore(operation, admittedAt, binding, work);
    }

    private void ValidateResourceEffectSubmissionCore(ReplicatedOperation operation, DateTimeOffset admittedAt,
        IResourceLifetimeWorkBinding? binding, ReadExecutionBudget? work)
    {
        work?.Check();
        operation = VerifyOperationAuthority(operation);
        work?.Check();
        _ = Store.Read(raw =>
        {
            var view = work is null ? raw : work.CreateView(raw);
            var current = new ResourceEffectBindingScope(OperationLimitsOptions);
            var observed = new ResourceEffectObservationView(view, current);
            var principal = Principal(observed, operation.PrincipalId, admittedAt);
            if (!AuthorizeResourceEffectSubmission(observed, principal, operation))
            {
                return true;
            }
            var scope = CommandOutcomePartitionIdentity.Resolve(operation);
            var selected = CommandOutcomeKeyResolver.Select(observed, operation.PrincipalId, operation.Id, scope);
            if (selected.Outcome is not null)
            {
                _ = ReadValidatedSubmissionOutcome(observed, operation);
                binding?.Bind(current.Capture());
                return true;
            }
            RequirePreparedResourceRetirementSubmission(observed, operation, admittedAt);
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
                current.Activate(observed, admission);
                binding?.Bind(current.Capture());
            }
            return true;
        });
        work?.Check();
    }

    private bool AuthorizeResourceEffectSubmission(IKeyValueView view, PrincipalRecord principal,
        ReplicatedOperation operation)
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
