using System.Collections.Immutable;
using KeyLoad.Core.CommandOutcomes.Execution;
using KeyLoad.Core.CommandOutcomes.Identity;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes StoredCommandRead operations against the caller's database context.</summary>
public static class StoredCommandRead
{
    internal const string StoredCommandScopeInvalid = "The retained command does not match its committed canonical outcome.";
    internal const string LastCommandScopeInvalid = "The last canonical outcome does not match the committed apply watermark.";
    /// <summary>Reads the exact original command for a freshly authorized retry of the same identity and content.</summary>
    /// <param name = "operation">Server-bound retry with the same stable command identity and original fingerprint.</param>
    /// <param name = "cancellationToken">Bounds the synchronous retained lookup.</param>
    /// <returns>The owned original command and its actual sequence, or a not-found result if it was never committed here.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static (bool HasValue, StoredCommand? Value) ReadStoredCommand(this DatabaseEngine engine, Command<DatabaseCommand> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var original = engine.OwnCommand(operation);
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
            _ = engine.ReadValidatedSubmissionOutcome(view, original, verifiedSelection: selected);
            StorageOperationApply.RequireStoredCanonicalSequence(view, outcome, siloLocal: false);
            var retained = engine.OwnCommand(outcome.PreparedCommand);
            if (retained.CommandId != original.CommandId || retained.UserId != original.UserId || retained.Value!.Kind != original.Value!.Kind || retained.Value.Fingerprint != outcome.Fingerprint)
            {
                throw Errors.Fail(ErrorCode.Corruption, StoredCommandScopeInvalid);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return (true, new StoredCommand(outcome.CanonicalSequence, retained));
        });
    }

    /// <summary>Reads the commands of the last canonical commit here so a later writer can complete a copy that missed it.</summary>
    /// <param name = "engine">The shared database execution context.</param>
    /// <param name = "cancellationToken">Bounds the synchronous retained lookup.</param>
    /// <returns>The owned commands of the last commit in sequence order, ending at the committed sequence; one command
    /// when that commit persisted one outcome, and none when no canonical command recorded its outcome key.</returns>
    public static ImmutableArray<StoredCommand> ReadLastCommittedCommands(this DatabaseEngine engine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.Store.Read(view =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var applied = StorageOperationApply.ReadCanonicalSequence(view);
            if (applied == StorageOperationApply.InitialCanonicalSequence
                || view.GetRecord<byte[]>(KeySpace.LastCommandBytes) is not { } outcomeKey)
            {
                return [];
            }

            var commands = view.GetRecord<byte[][]>(KeySpace.LastBatchBytes) is { } batch
                ? engine.ReadLastBatch(view, batch, outcomeKey, applied) : [];
            cancellationToken.ThrowIfCancellationRequested();
            return commands.IsEmpty ? [engine.ReadCommitted(view, outcomeKey, applied)] : commands;
        });
    }

    /// <summary>Reads the retained batch only when it is the last commit: its final outcome is the last command.</summary>
    private static ImmutableArray<StoredCommand> ReadLastBatch(this DatabaseEngine engine, IKeyValueView view,
        byte[][] outcomeKeys, byte[] lastOutcomeKey, long applied)
    {
        if (outcomeKeys.Length == 0 || !outcomeKeys[^1].AsSpan().SequenceEqual(lastOutcomeKey))
        {
            return [];
        }

        var commands = ImmutableArray.CreateBuilder<StoredCommand>(outcomeKeys.Length);
        var sequence = applied - outcomeKeys.Length;
        foreach (var outcomeKey in outcomeKeys)
        {
            commands.Add(engine.ReadCommitted(view, outcomeKey, ++sequence));
        }

        return commands.MoveToImmutable();
    }

    private static StoredCommand ReadCommitted(this DatabaseEngine engine, IKeyValueView view, byte[] outcomeKey, long sequence)
    {
        var outcome = view.GetRecord<StoredOutcome>(outcomeKey)
            ?? throw Errors.Fail(ErrorCode.Corruption, LastCommandScopeInvalid);
        if (outcome.CanonicalSequence != sequence)
        {
            throw Errors.Fail(ErrorCode.Corruption, LastCommandScopeInvalid);
        }

        var retained = engine.OwnCommand(outcome.PreparedCommand);
        if (retained.Value!.Fingerprint != outcome.Fingerprint)
        {
            throw Errors.Fail(ErrorCode.Corruption, StoredCommandScopeInvalid);
        }

        return new StoredCommand(sequence, retained);
    }
}
