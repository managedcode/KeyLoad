using KeyLoad.Core.CommandOutcomes.Execution;
using KeyLoad.Core.CommandOutcomes.Identity;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Core;
/// <summary>Executes ResourceExecutionExecution operations against the caller's database context.</summary>
public static class ResourceExecutionExecution
{
    internal static Result<object> ResolveAppliedSubmissionOutcome(this DatabaseEngine engine, Command<DatabaseCommand> operation, ReadExecutionBudget? work = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        work?.Check();
        var normalized = engine.OwnCommand(operation);
        var result = engine.Store.Read(raw =>
        {
            var view = work is null ? raw : work.CreateView(raw);
            try
            {
                var principal = engine.Principal(view, normalized.UserId!, engine.Clock.GetUtcNow());
                _ = (engine.AuthorizeOperation(view, principal, normalized)).Value;
                var scope = CommandOutcomePartitionIdentity.Resolve(normalized);
                var selection = CommandOutcomeKeyResolver.Select(view, normalized.UserId!, normalized.CommandId, scope);
                var outcome = selection.Outcome ?? throw Errors.Fail(ErrorCode.RecoveryRequired, CommandOutcomeOperations.MissingDurableOutcomeMessage);
                engine.ValidateStoredSubmissionIdentity(view, normalized, selection, outcome);
                return engine.ReadValidatedSubmissionOutcome(view, normalized, verifiedSelection: selection);
            }
            catch (KeyLoadException refusal) when (CqrsRuntimeFailures.FindFatal(refusal) is null && refusal.Code is ErrorCode.PermissionDenied or ErrorCode.Unauthenticated or ErrorCode.TokenInvalidated or ErrorCode.StaleLease or ErrorCode.LeaseExpired or ErrorCode.Conflict)
            {
                return ManagedCode.Communication.Result<object>.Fail(Errors.Problem(refusal.Code, refusal.Message));
            }
        });
        work?.CheckResult(result.Value ?? result);
        work?.Check();
        return result;
    }

    internal static void ValidateStoredSubmissionIdentity(this DatabaseEngine engine, IKeyValueView view, Command<DatabaseCommand> operation, CommandOutcomeSelection selection, StoredOutcome outcome)
    {
        if (outcome.Incarnation != engine.Store.Identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, CommandOutcomeOperations.EarlierOutcomeIncarnationMessage);
        }

        DatabaseCommandContentIdentity.RequireOriginalCommandContent(operation, outcome);
        CommandOutcomeKeyResolver.ValidateSelectedScope(view, operation, selection);
    }
}
