using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal Result<object> ResolveAppliedSubmissionOutcome(Command<DatabaseCommand> operation, ReadExecutionBudget? work = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        work?.Check();
        var normalized = OwnCommand(operation);
        var result = Store.Read(raw =>
        {
            var view = work is null ? raw : work.CreateView(raw);
            try
            {
                var principal = Principal(view, normalized.UserId!, Clock.GetUtcNow());
                AuthorizeOperation(view, principal, normalized);
                var scope = CommandOutcomePartitionIdentity.Resolve(normalized);
                var selection = CommandOutcomeKeyResolver.Select(view, normalized.UserId!, normalized.CommandId, scope);
                var outcome = selection.Outcome
                    ?? throw Errors.Fail(ErrorCode.RecoveryRequired, MissingDurableOutcomeMessage);
                ValidateStoredSubmissionIdentity(view, normalized, selection, outcome);
                return ReadValidatedSubmissionOutcome(view, normalized, verifiedSelection: selection);
            }
            catch (KeyLoadException refusal) when (CqrsRuntimeFailures.FindFatal(refusal) is null
                && refusal.Code is ErrorCode.PermissionDenied or ErrorCode.TokenInvalidated
                    or ErrorCode.StaleLease or ErrorCode.LeaseExpired or ErrorCode.Conflict)
            {
                return ManagedCode.Communication.Result<object>.Fail(Errors.Problem(refusal.Code, refusal.Message));
            }
        });
        work?.CheckResult(result.Value ?? result);
        work?.Check();
        return result;
    }

    private void ValidateStoredSubmissionIdentity(IKeyValueView view, Command<DatabaseCommand> operation,
        CommandOutcomeSelection selection, StoredOutcome outcome)
    {
        if (outcome.Incarnation != Store.Identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, EarlierOutcomeIncarnationMessage);
        }
        RequireOriginalCommandContent(operation, outcome);
        CommandOutcomeKeyResolver.ValidateSelectedScope(view, operation, selection);
    }
}
