using KeyLoad.Core.Features.CommandOutcomes.Execution;
using KeyLoad.Core.Features.CommandOutcomes.Identity;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string StoredCommandScopeInvalid = "The retained command does not match its committed canonical outcome.";

    /// <summary>Reads the exact original command for a freshly authorized retry of the same identity and content.</summary>
    /// <param name="operation">Server-bound retry with the same stable command identity and original fingerprint.</param>
    /// <param name="cancellationToken">Bounds the synchronous retained lookup.</param>
    /// <returns>The owned original command and its actual sequence, or null if it was never committed here.</returns>
    public StoredCommand? ReadStoredCommand(Command<DatabaseCommand> operation, CancellationToken cancellationToken)
    {
        var original = OwnCommand(operation);
        return Store.Read<StoredCommand?>(view =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var principal = Principal(view, original.UserId!, Clock.GetUtcNow());
            AuthorizeOperation(view, principal, original);
            var scope = CommandOutcomePartitionIdentity.Resolve(original);
            var selected = CommandOutcomeKeyResolver.Select(view, original.UserId!, original.CommandId, scope);
            if (selected.Outcome is not { } outcome)
            {
                return null;
            }
            ValidateStoredSubmissionIdentity(view, original, selected, outcome);
            _ = ReadValidatedSubmissionOutcome(view, original, verifiedSelection: selected);
            RequireStoredCanonicalSequence(view, outcome, siloLocal: false);
            var retained = OwnCommand(outcome.PreparedCommand);
            if (retained.CommandId != original.CommandId || retained.UserId != original.UserId
                || retained.Value!.Kind != original.Value!.Kind || retained.Value.Fingerprint != outcome.Fingerprint)
            {
                throw Errors.Fail(ErrorCode.Corruption, StoredCommandScopeInvalid);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new StoredCommand(outcome.CanonicalSequence, retained);
        });
    }
}
