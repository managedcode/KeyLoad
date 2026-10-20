using KeyLoad.Core.Features.CommandOutcomes.Execution;
using KeyLoad.Core.Features.CommandOutcomes.Identity;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes StorageExecutionExecutionStoredCommandRead operations against the caller's database context.</summary>
public static class StorageExecutionExecutionStoredCommandRead
{
    internal const string StoredCommandScopeInvalid = "The retained command does not match its committed canonical outcome.";
    /// <summary>Reads the exact original command for a freshly authorized retry of the same identity and content.</summary>
    /// <param name = "operation">Server-bound retry with the same stable command identity and original fingerprint.</param>
    /// <param name = "cancellationToken">Bounds the synchronous retained lookup.</param>
    /// <returns>The owned original command and its actual sequence, or null if it was never committed here.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static StoredCommand? ReadStoredCommand(this DatabaseEngine engine, Command<DatabaseCommand> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var original = engine.OwnCommand(operation);
        return engine.Store.Read<StoredCommand?>(view =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var principal = engine.Principal(view, original.UserId!, engine.Clock.GetUtcNow());
            engine.AuthorizeOperation(view, principal, original);
            var scope = CommandOutcomePartitionIdentity.Resolve(original);
            var selected = CommandOutcomeKeyResolver.Select(view, original.UserId!, original.CommandId, scope);
            if (selected.Outcome is not { } outcome)
            {
                return null;
            }

            engine.ValidateStoredSubmissionIdentity(view, original, selected, outcome);
            _ = engine.ReadValidatedSubmissionOutcome(view, original, verifiedSelection: selected);
            StorageExecutionExecutionStorageOperationApply.RequireStoredCanonicalSequence(view, outcome, siloLocal: false);
            var retained = engine.OwnCommand(outcome.PreparedCommand);
            if (retained.CommandId != original.CommandId || retained.UserId != original.UserId || retained.Value!.Kind != original.Value!.Kind || retained.Value.Fingerprint != outcome.Fingerprint)
            {
                throw Errors.Fail(ErrorCode.Corruption, StoredCommandScopeInvalid);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new StoredCommand(outcome.CanonicalSequence, retained);
        });
    }
}
