using KeyLoad.Core.CommandOutcomes.Execution;
using KeyLoad.Core.CommandOutcomes.Identity;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes StoredCommandRead operations against the caller's database context.</summary>
public static class StoredCommandRead
{
    internal const string StoredCommandScopeInvalid = "The retained command does not match its committed canonical outcome.";
    /// <summary>Reads the exact original command for a freshly authorized retry of the same identity and content.</summary>
    /// <param name = "operation">Server-bound retry with the same stable command identity.</param>
    /// <param name = "cancellationToken">Bounds the synchronous retained lookup.</param>
    /// <returns>The owned original command and its actual sequence, or a not-found result if it was never committed here.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static (bool HasValue, StoredCommand? Value) ReadStoredCommand(this DatabaseEngine engine, Command<DatabaseCommand> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        return engine.ReadOwnedStoredCommand(engine.OwnCommand(operation), cancellationToken);
    }

    /// <summary>Reads the retained outcome for the caller's already owned command.</summary>
    internal static (bool HasValue, StoredCommand? Value) ReadOwnedStoredCommand(this DatabaseEngine engine,
        Command<DatabaseCommand> original, CancellationToken cancellationToken)
    {
        return engine.Store.Read<(bool HasValue, StoredCommand? Value)>(view =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scope = CommandOutcomePartitionIdentity.Resolve(original);
            if (CommandOutcomeKeyResolver.IsUnstored(view, original.UserId!, original.CommandId, scope))
            {
                // A new command is authorized when it is applied, so a denial persists its outcome exactly once and a
                // retry after a policy change replays that denial instead of running as a new command.
                return (false, default(StoredCommand?));
            }

            var principal = engine.Principal(view, original.UserId!, engine.Clock.GetUtcNow());
            _ = (engine.AuthorizeOperation(view, principal, original)).Value;
            var selected = CommandOutcomeKeyResolver.Select(view, original.UserId!, original.CommandId, scope);
            if (selected.Outcome is not { } outcome)
            {
                return (false, default(StoredCommand?));
            }

            engine.ValidateStoredSubmissionIdentity(view, original, selected, outcome);
            _ = (engine.ReadValidatedSubmissionOutcome(view, original, verifiedSelection: selected, verifiedPrincipal: principal) /* KLD0041: This step reads the existing native persisted outcome after identity and authority checks while retaining its original heterogeneous payload. */);
            StorageOperationApply.RequireStoredCanonicalSequence(view, outcome, siloLocal: false);
            var retained = engine.OwnCommand(outcome.PreparedCommand);
            if (retained.CommandId != original.CommandId || retained.UserId != original.UserId || retained.Value!.Kind != original.Value!.Kind)
            {
                throw Errors.Fail(ErrorCode.Corruption, StoredCommandScopeInvalid);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return (true, new StoredCommand(outcome.CanonicalSequence, retained));
        });
    }

}
