using ManagedCode.Communication.Commands;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Declines speculative eligibility failures without replacing original admission.</summary>
    internal static CommandRequest? TryReadOrdinaryDocumentSubmission(Command<DatabaseCommand> operation)
    {
        if (operation.Value!.Kind != OperationKind.Batch)
        {
            return null;
        }
        try
        {
            return ReadOrdinaryDocumentSubmission(operation);
        }
        catch (KeyLoadException) { return null; }
    }

    /// <summary>Normalizes eligible direct calls; production passes its already verified candidate.</summary>
    internal void ValidateSubmissionAdmission(Command<DatabaseCommand> operation)
    {
        if (operation.Value!.Kind != OperationKind.Batch)
        {
            return;
        }
        Command<DatabaseCommand> normalized;
        try
        {
            normalized = OwnCommand(operation);
        }
        catch (KeyLoadException) { return; }
        if (TryReadOrdinaryDocumentSubmission(normalized) is not null)
        {
            ValidateSubmissionAdmissionCore(normalized, null);
        }
    }

    /// <summary>Checks authorized changed identity using the original verified operation.</summary>
    internal void ValidateVerifiedSubmissionAdmission(Command<DatabaseCommand> operation)
        => ValidateSubmissionAdmissionCore(operation, null);

    /// <summary>Checks authorized changed identity using the original verified operation and bounded work.</summary>
    internal void ValidateSubmissionAdmission(Command<DatabaseCommand> operation, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        ValidateSubmissionAdmissionCore(operation, work);
        work.Check();
    }

    private void ValidateSubmissionAdmissionCore(Command<DatabaseCommand> operation, ReadExecutionBudget? work)
    {
        _ = Store.Read(raw =>
        {
            var view = work is null ? raw : work.CreateView(raw);
            if (!TryAuthorizeOrdinaryDocumentSubmission(view, operation, out var principal, out var denial, work is not null)
                || principal is null)
            {
                if (principal is not null && denial is not null
                    && HasChangedRetainedDocumentDenial(view, principal, operation, denial, work is not null))
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(denial).Throw();
                }
                return false;
            }
            var scope = CommandOutcomePartitionIdentity.Resolve(operation);
            var selection = CommandOutcomeKeyResolver.Select(view, operation.UserId!, operation.CommandId, scope);
            if (selection.Outcome is not { } outcome || outcome.PolicyEpoch != principal.PolicyEpoch
                || outcome.Incarnation != Store.Identity.Incarnation
                || outcome.Fingerprint == CommandFingerprint(operation))
            {
                return false;
            }
            ValidateSubmissionOutcomeIdentity(view, principal, operation, selection, outcome);
            return true;
        });
    }

    private bool TryAuthorizeOrdinaryDocumentSubmission(IKeyValueView view, Command<DatabaseCommand> operation,
        out PrincipalRecord? principal, out KeyLoadException? denial, bool bounded = false)
    {
        principal = null;
        denial = null;
        try
        {
            principal = Principal(view, operation.UserId!, Clock.GetUtcNow());
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Unauthenticated) { throw; }
        catch (KeyLoadException error) when (CanDeclineDocumentAdmission(error, bounded)) { return false; }
        try
        {
            AuthorizeOperation(view, principal, operation);
            return true;
        }
        catch (KeyLoadException error) when (CanDeclineDocumentAdmission(error, bounded))
        {
            if (error.Code == ErrorCode.PermissionDenied)
            {
                denial = error;
            }
            return false;
        }
    }

    private bool HasChangedRetainedDocumentDenial(IKeyValueView view, PrincipalRecord principal,
        Command<DatabaseCommand> operation, KeyLoadException denial, bool bounded)
    {
        try
        {
            if (ReadOrdinaryDocumentSubmission(operation) is null)
            {
                return false;
            }
            var scope = CommandOutcomePartitionIdentity.Resolve(operation);
            var selection = CommandOutcomeKeyResolver.Select(view, operation.UserId!, operation.CommandId, scope);
            if (selection.Outcome is not { } outcome || outcome.PolicyEpoch != principal.PolicyEpoch
                || outcome.Incarnation != Store.Identity.Incarnation || string.IsNullOrEmpty(outcome.Fingerprint)
                || outcome.Fingerprint == CommandFingerprint(operation))
            {
                return false;
            }
            CommandOutcomeKeyResolver.ValidateSelectedScope(view, operation, selection);
            var result = outcome.Result;
            return Errors.Code(result.Problem) == denial.Code
                && string.Equals(result.Problem?.Detail, denial.Message, StringComparison.Ordinal)
                && result.Value is null
                && outcome.BlobAuthority is null && outcome.CompositionAuthority is null
                && outcome.OnlineTextAuthority is null && outcome.RemoteTransferFailureAuthority is null
                ;
        }
        catch (KeyLoadException error) when (CanDeclineDocumentAdmission(error, bounded)) { return false; }
    }

    private static bool CanDeclineDocumentAdmission(KeyLoadException error, bool bounded)
        => !bounded || error.Code is not (ErrorCode.BudgetExceeded or ErrorCode.Corruption
            or ErrorCode.FormatUnsupported or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome);

    private static CommandRequest? ReadOrdinaryDocumentSubmission(Command<DatabaseCommand> operation)
    {
        if (operation.Value!.Kind != OperationKind.Batch)
        {
            return null;
        }
        var command = Payload<CommandRequest>(operation);
        return !command.Mutations.IsDefaultOrEmpty
            && command.Mutations.All(static mutation => mutation is InsertTypedRow or PutDocument or PatchDocument or DeleteDocument)
                ? command : null;
    }
}
