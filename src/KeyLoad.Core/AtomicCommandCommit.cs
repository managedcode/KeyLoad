using System.Text.Json;
using KeyLoad.Core.Features.BackupRestore.Execution;
using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.CommandOutcomes.Execution;
using KeyLoad.Core.Features.CommandOutcomes.Identity;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes AtomicCommandCommit operations against the caller's database context.</summary>
public static class AtomicCommandCommit
{
    internal const int AtomicCommandCommitInitialSequence = 0;
    internal const string InvalidCommandBudgetMessage = "The operation ID or byte budget is invalid.";
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
    {
        ArgumentNullException.ThrowIfNull(engine);
        cancellationToken.ThrowIfCancellationRequested();
        var verified = engine.OwnCommand(operation);
        var prepared = engine.Store.Read(view =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = engine.EvaluationClock.GetUtcNow();
            if (view.ReadOwnedValue(KeySpace.ClockBytes) is { } encoded)
            {
                var committed = OrleansSerialization.Deserialize<DateTimeOffset>(encoded);
                now = now < committed ? committed : now;
            }

            return engine.PrepareQueueRetryInView(view, engine.EvaluateCommand(verified, now));
        });
        return prepared;
    }

    internal static StorageOperationResult ApplyCore(this DatabaseEngine engine, Command<DatabaseCommand> operation, long? canonicalSequence, bool selectEmbeddedClock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        operation = engine.OwnCommand(operation);
        var siloLocal = engine.IsSiloLocalOperation(operation);
        cancellationToken.ThrowIfCancellationRequested();
        var committed = engine.Store.Commit((transaction, position) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var admitted = selectEmbeddedClock ? engine.EvaluateCommand(operation, engine.EvaluationClock.GetUtcNow()) : operation;
            if (selectEmbeddedClock)
            {
                admitted = engine.PrepareQueueRetryInView(transaction, admitted);
            }

            var sequence = siloLocal ? StorageExecutionExecutionStorageOperationApply.InitialCanonicalSequence : canonicalSequence ?? StorageExecutionExecutionStorageOperationApply.SelectNextCanonicalSequence(transaction);
            var result = engine.ApplyCommittedCommand(transaction, admitted, position, sequence, siloLocal, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        });
        return committed;
    }

    internal static StorageOperationResult ApplyCommittedCommand(this DatabaseEngine engine, IAtomicTransaction transaction, Command<DatabaseCommand> operation, long position, long replicationIndex, bool siloLocal, CancellationToken cancellationToken)
    {
        var rosterTransaction = new AtomicPartitionRosterTransaction(transaction, engine.OperationLimitsOptions, position, replicationIndex);
        var partitionScope = CommandOutcomePartitionIdentity.Resolve(operation);
        var fingerprint = OperationProtocol.CommandFingerprint(operation);
        var outcome = engine.ExecuteAndBuildOutcome(rosterTransaction, operation, position, replicationIndex, fingerprint, partitionScope, siloLocal, out var replayed, out var selection, out var persistOutcome, cancellationToken);
        if (replayed || !persistOutcome)
        {
            return new(outcome.CanonicalSequence, outcome.Result);
        }

        if (!siloLocal)
        {
            StorageExecutionExecutionStorageOperationApply.RequireNextCanonicalSequence(rosterTransaction, replicationIndex);
        }

        outcome = outcome with
        {
            CanonicalSequence = replicationIndex
        };
        CommandOutcomeApplication.PersistCommandOutcome(rosterTransaction, operation, selection.Key, outcome, replicationIndex, persistOutcome);
        var result = CommandOutcomeApplication.ValidateCompiledCommand(rosterTransaction, operation, selection.Key, outcome, replicationIndex, persistOutcome);
        return new(outcome.CanonicalSequence, result);
    }

    internal static StoredOutcome ExecuteAndBuildOutcome(this DatabaseEngine engine, AtomicPartitionRosterTransaction transaction, Command<DatabaseCommand> operation, long position, long replicationIndex, string fingerprint, CommandOutcomePartitionScope partitionScope, bool siloLocal, out bool replayed, out CommandOutcomeSelection selection, out bool persistOutcome, CancellationToken cancellationToken)
    {
        replayed = false;
        selection = CommandOutcomeKeyResolver.ForNew(operation.UserId!, operation.CommandId, partitionScope);
        persistOutcome = false;
        var authorizationPending = false;
        long policyEpoch = AtomicCommandCommitInitialSequence;
        var onlineTextAuthority = SearchCommandsOnlineTextPublicationCommands.CaptureOnlineTextOutcomeAuthority(operation);
        global::KeyLoad.Core.Features.Messaging.RemoteTransferAcceptFailureAuthority? transferFailure = null;
        Result<object> result;
        global::KeyLoad.Core.Features.DatabaseComposition.CompositionOutcomeAuthority? compositionAuthority = null;
        try
        {
            var principal = engine.Principal(transaction, operation.UserId!, engine.Clock.GetUtcNow());
            authorizationPending = true;
            policyEpoch = principal.PolicyEpoch;
            var placement = engine.AuthorizeOperation(transaction, principal, operation);
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
                StorageExecutionExecutionStorageOperationApply.RequireNextCanonicalSequence(transaction, replicationIndex);
            }

            engine.RequireOriginalEffectAdmission(transaction, principal, operation);
            ValidateCommandClock(transaction, operation.Timestamp);
            engine.ValidateQueueRetryDecisions(transaction, principal, operation);
            transferFailure = engine.CaptureRemoteTransferFailureAuthority(transaction, principal, operation);
            result = engine.Execute(transaction, principal, operation, replicationIndex > AtomicCommandCommitInitialSequence ? replicationIndex : position, placement, cancellationToken);
            compositionAuthority = engine.CaptureCompositionOutcome(transaction, principal, operation, result);
        }
        catch (KeyLoadException exception) when (exception.Code is not (ErrorCode.Corruption or ErrorCode.FormatUnsupported or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome))
        {
            if (authorizationPending)
            {
                persistOutcome = CommandOutcomeKeyResolver.IsUnstored(transaction, operation.UserId!, operation.CommandId, partitionScope);
            }

            transaction.Reset();
            replayed = false;
            result = ManagedCode.Communication.Result<object>.Fail(Errors.Problem(exception.Code, exception.Message));
        }
        catch (JsonException)
        {
            transaction.Reset();
            replayed = false;
            result = ManagedCode.Communication.Result<object>.Fail(Errors.Problem(ErrorCode.Validation, InvalidCommandJsonMessage));
        }

        return engine.BuildStoredOutcome(operation, fingerprint, policyEpoch, result, compositionAuthority, partitionScope, onlineTextAuthority, transferFailure, selection.Outcome?.CanonicalSequence ?? (persistOutcome ? replicationIndex : StorageExecutionExecutionStorageOperationApply.ReadCanonicalSequence(transaction)));
    }

    internal static void ValidateCommandClock(AtomicPartitionRosterTransaction view, DateTimeOffset evaluatedAt)
    {
        if (view.ReadOwnedValue(KeySpace.ClockBytes) is { } clock && evaluatedAt < OrleansSerialization.Deserialize<DateTimeOffset>(clock))
        {
            throw Errors.Fail(ErrorCode.ClockUncertain, CommittedClockAheadMessage);
        }
    }

}
