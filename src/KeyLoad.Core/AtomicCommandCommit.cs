using System.Text.Json;
using KeyLoad.Core.BackupRestore.Execution;
using KeyLoad.Core.CommandOutcomes.Contracts;
using KeyLoad.Core.CommandOutcomes.Execution;
using KeyLoad.Core.CommandOutcomes.Identity;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes AtomicCommandCommit operations against the caller's database context.</summary>
public static class AtomicCommandCommit
{
    internal const int AtomicCommandCommitInitialSequence = 0;
    internal const string PreviousCommandIncarnationMessage = "This command belongs to a previous database incarnation.";
    internal const string CommandContentConflictMessage = "The command ID was already used with different content.";
    internal const string CommittedClockAheadMessage = "The operation time is behind the committed storage time.";
    internal const string InvalidCommandJsonMessage = "The operation contains invalid protocol JSON.";
    internal const string MissingOutcomePartitionMessage = "A partition-scoped command outcome has no partition identity.";
    /// <summary>Prepares one authorized operation once before sending identical bytes to storage copies.</summary>

    /// <param name = "engine">The shared database execution context.</param>
    /// <param name = "operation">The operation.</param>
    /// <param name = "cancellationToken">The cancellation Token.</param>
    public static Command<DatabaseCommand> PrepareStorageOperation(this DatabaseEngine engine, Command<DatabaseCommand> operation, CancellationToken cancellationToken)
        => engine.PrepareStorageOperation(operation, null, cancellationToken);

    /// <summary>Prepares one operation at a business time no earlier than the committed clock or <paramref name="notBefore"/>.</summary>
    internal static Command<DatabaseCommand> PrepareStorageOperation(this DatabaseEngine engine, Command<DatabaseCommand> operation, DateTimeOffset? notBefore, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        cancellationToken.ThrowIfCancellationRequested();
        return engine.PrepareOwnedStorageOperation(engine.OwnCommand(operation), notBefore, cancellationToken);
    }

    /// <summary>Prepares the already owned operation under the committed clock.</summary>
    internal static Command<DatabaseCommand> PrepareOwnedStorageOperation(this DatabaseEngine engine,
        Command<DatabaseCommand> owned, DateTimeOffset? notBefore, CancellationToken cancellationToken)
    {
        var prepared = engine.Store.Read(view =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = engine.EvaluationClock.GetUtcNow();
            if (view.ReadOwnedValue(KeySpace.ClockBytes) is { } encoded)
            {
                var committed = OrleansSerialization.Deserialize<DateTimeOffset>(encoded);
                now = now < committed ? committed : now;
            }

            if (notBefore is { } floor && now < floor)
            {
                now = floor;
            }

            owned.Timestamp = now.UtcDateTime;
            return engine.PrepareQueueRetryInView(view, owned);
        });
        return prepared;
    }

    internal static StorageOperationResult ApplyCore(this DatabaseEngine engine, Command<DatabaseCommand> operation, long? canonicalSequence, bool selectEmbeddedClock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        operation = engine.OwnCommand(operation);
        var siloLocal = engine.IsSiloLocalOperation(operation);
        cancellationToken.ThrowIfCancellationRequested();
        var committed = engine.Store.Commit((transaction, position) =>
            engine.CompileCommand(transaction, position, operation, canonicalSequence, selectEmbeddedClock, siloLocal, cancellationToken));
        return committed;
    }

    internal static Task<StorageOperationResult> ApplyCoreAsync(this DatabaseEngine engine, Command<DatabaseCommand> operation,
        long? canonicalSequence, bool selectEmbeddedClock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        operation = engine.OwnCommand(operation);
        var siloLocal = engine.IsSiloLocalOperation(operation);
        return engine.Store.CommitAsync((transaction, position) =>
            engine.CompileCommand(transaction, position, operation, canonicalSequence, selectEmbeddedClock, siloLocal, cancellationToken),
            cancellationToken);
    }

    private static StorageOperationResult CompileCommand(this DatabaseEngine engine, IAtomicTransaction transaction,
        long position, Command<DatabaseCommand> operation, long? canonicalSequence, bool selectEmbeddedClock, bool siloLocal,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var admitted = selectEmbeddedClock ? engine.EvaluateCommand(operation, engine.EvaluationClock.GetUtcNow()) : operation;
        if (selectEmbeddedClock)
        {
            admitted = engine.PrepareQueueRetryInView(transaction, admitted);
        }
        var sequence = siloLocal ? StorageOperationApply.InitialCanonicalSequence
            : canonicalSequence ?? StorageOperationApply.SelectNextCanonicalSequence(transaction);
        var result = engine.ApplyCommittedCommand(transaction, admitted, position, sequence, siloLocal, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    internal static StorageOperationResult ApplyCommittedCommand(this DatabaseEngine engine, IAtomicTransaction transaction, Command<DatabaseCommand> operation, long position, long replicationIndex, bool siloLocal, CancellationToken cancellationToken)
    {
        var rosterTransaction = new AtomicPartitionRosterTransaction(transaction, position, replicationIndex);
        var partitionScope = CommandOutcomePartitionIdentity.Resolve(operation);
        var outcome = engine.ExecuteAndBuildOutcome(rosterTransaction, operation, position, replicationIndex, partitionScope, siloLocal, out var replayed, out var selection, out var persistOutcome, cancellationToken);
        if (replayed || !persistOutcome)
        {
            return new(outcome.CanonicalSequence, outcome.Result /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */) /* KLD0041: This constructor preserves the original native mixed-operation result in its existing persisted storage contract. */;
        }

        if (!siloLocal)
        {
            StorageOperationApply.RequireNextCanonicalSequence(rosterTransaction, replicationIndex);
        }

        outcome = outcome with
        {
            CanonicalSequence = replicationIndex
        };
        CommandOutcomeApplication.PersistCommandOutcome(rosterTransaction, operation, selection.Key, outcome, replicationIndex, persistOutcome);
        rosterTransaction.PersistCandidates();
        return new(outcome.CanonicalSequence, outcome.Result /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */) /* KLD0041: This constructor preserves the original native mixed-operation result in its existing persisted storage contract. */;
    }

    internal static StoredOutcome ExecuteAndBuildOutcome(this DatabaseEngine engine, AtomicPartitionRosterTransaction transaction, Command<DatabaseCommand> operation, long position, long replicationIndex, CommandOutcomePartitionScope partitionScope, bool siloLocal, out bool replayed, out CommandOutcomeSelection selection, out bool persistOutcome, CancellationToken cancellationToken)
    {
        replayed = false;
        selection = CommandOutcomeKeyResolver.ForNew(operation.UserId!, operation.CommandId, partitionScope);
        persistOutcome = false;
        var authorizationPending = false;
        long policyEpoch = AtomicCommandCommitInitialSequence;
        var onlineTextAuthority = (OnlineTextPublicationCommands.CaptureOnlineTextOutcomeAuthority(operation)).Value;
        global::KeyLoad.Core.Messaging.RemoteTransferAcceptFailureAuthority? transferFailure = null;
        Result<object> /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */ result;
        global::KeyLoad.Core.DatabaseComposition.CompositionOutcomeAuthority? compositionAuthority = null;
        try
        {
            var principal = engine.Principal(transaction, operation.UserId!, engine.Clock.GetUtcNow());
            authorizationPending = true;
            policyEpoch = principal.PolicyEpoch;
            var placement = (engine.AuthorizeOperation(transaction, principal, operation)).Value;
            authorizationPending = false;
            selection = CommandOutcomeKeyResolver.Select(transaction, operation.UserId!, operation.CommandId, partitionScope);
            persistOutcome = selection.Outcome is null;
            if (selection.Outcome is { } previous)
            {
                replayed = true;
                return engine.ReplayCommand(transaction, operation, previous, selection, siloLocal);
            }

            if (!siloLocal)
            {
                StorageOperationApply.RequireNextCanonicalSequence(transaction, replicationIndex);
            }

            engine.RequireOriginalEffectAdmission(transaction, principal, operation);
            ValidateCommandClock(transaction, operation.Timestamp);
            engine.ValidateQueueRetryDecisions(transaction, principal, operation);
            transferFailure = (engine.CaptureRemoteTransferFailureAuthority(transaction, principal, operation)).Value;
            result = (engine.Execute(transaction, principal, operation, replicationIndex > AtomicCommandCommitInitialSequence ? replicationIndex : position, placement, cancellationToken) /* KLD0041: The dispatcher places the typed business outcome in the existing native mixed-operation stored-result schema. */);
            compositionAuthority = (engine.CaptureCompositionOutcome(transaction, principal, operation, result) /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */).Value;
        }
        catch (KeyLoadException exception) when (exception.Code is not (ErrorCode.Corruption or ErrorCode.FormatUnsupported or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome))
        {
            if (authorizationPending)
            {
                persistOutcome = CommandOutcomeKeyResolver.IsUnstored(transaction, operation.UserId!, operation.CommandId, partitionScope);
            }

            transaction.Reset();
            replayed = false;
            result = (ManagedCode.Communication.Result<object>.Fail(Errors.Problem(exception.Code, exception.Message)) /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */);
        }
        catch (JsonException)
        {
            transaction.Reset();
            replayed = false;
            result = (ManagedCode.Communication.Result<object>.Fail(Errors.Problem(ErrorCode.Validation, InvalidCommandJsonMessage)) /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */);
        }

        return engine.BuildStoredOutcome(operation, policyEpoch, result, compositionAuthority, partitionScope, onlineTextAuthority, transferFailure, selection.Outcome?.CanonicalSequence ?? (persistOutcome ? replicationIndex : StorageOperationApply.ReadCanonicalSequence(transaction))) /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */;
    }

    internal static void ValidateCommandClock(AtomicPartitionRosterTransaction view, DateTimeOffset evaluatedAt)
    {
        if (view.ReadOwnedValue(KeySpace.ClockBytes) is { } clock && evaluatedAt < OrleansSerialization.Deserialize<DateTimeOffset>(clock))
        {
            throw Errors.Fail(ErrorCode.ClockUncertain, CommittedClockAheadMessage);
        }
    }

}
