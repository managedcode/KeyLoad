using KeyLoad.Core.Features.BackupRestore.Execution;
using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.CommandOutcomes.Execution;
using KeyLoad.Core.Features.CommandOutcomes.Serialization;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Owns the CommandOutcomeApplication operation contract.</summary>
internal static class CommandOutcomeApplication
{
    internal static StoredOutcome BuildStoredOutcome(this DatabaseEngine engine, Command<DatabaseCommand> operation, string fingerprint, long policyEpoch, Result<object> result, global::KeyLoad.Core.Features.DatabaseComposition.CompositionOutcomeAuthority? compositionAuthority, CommandOutcomePartitionScope partitionScope, global::KeyLoad.Core.Features.Search.OnlineTextOutcomeAuthority? onlineTextAuthority, global::KeyLoad.Core.Features.Messaging.RemoteTransferAcceptFailureAuthority? transferFailure, long canonicalSequence) => new(fingerprint, engine.Store.Identity.Incarnation, policyEpoch, result, operation)
    {
        CompositionAuthority = Errors.Code(result.Problem) is null ? compositionAuthority : null,
        ScopeKind = partitionScope.Kind,
        Partition = partitionScope.Partition,
        CanonicalSequence = canonicalSequence,
        OnlineTextAuthority = onlineTextAuthority,
        RemoteTransferFailureAuthority = MessagingExecutionRemoteTransferRemoteTransferFailureCapture.HasRemoteTransferCapacityFailure(result) ? transferFailure : null
    };
    internal static StoredOutcome ReplayCommand(this DatabaseEngine engine, IAtomicTransaction transaction, Command<DatabaseCommand> operation, StoredOutcome previous, CommandOutcomeSelection selection, bool siloLocal)
    {
        StorageExecutionExecutionStorageOperationApply.RequireStoredCanonicalSequence(transaction, previous, siloLocal);
        if (previous.Incarnation != engine.Store.Identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, AtomicCommandCommit.PreviousCommandIncarnationMessage);
        }

        StorageExecutionIdentityDatabaseCommandContentIdentity.RequireOriginalCommandContent(operation, previous);
        CommandOutcomeKeyResolver.ValidateSelectedScope(transaction, operation, selection);
        var current = engine.EvaluateCommand(operation, engine.Clock.GetUtcNow());
        var principal = engine.Principal(transaction, operation.UserId!, current.Timestamp);
        engine.AuthorizeOperation(transaction, principal, current);
        engine.ValidateCachedResult(transaction, principal, current, previous);
        return previous;
    }

    internal static void PersistCommandOutcome(AtomicPartitionRosterTransaction transaction, Command<DatabaseCommand> operation, byte[] resultKey, StoredOutcome outcome, long replicationIndex, bool persistOutcome)
    {
        // Domain effects, outcome and apply watermark share one redo transaction.
        var outcomeAlreadyStored = persistOutcome && transaction.ReadOwnedValue(resultKey) is not null;
        if (persistOutcome && !outcomeAlreadyStored)
        {
            transaction.PutRecord(resultKey, outcome);
            if (outcome.ScopeKind == CommandOutcomeScopeKind.Partition)
            {
                if (outcome.Partition is null)
                {
                    throw Errors.Fail(ErrorCode.Corruption, AtomicCommandCommit.MissingOutcomePartitionMessage);
                }

                CommandOutcomePartitionLocatorSerialization.WriteScoped(transaction, outcome.Partition, operation.UserId!, operation.CommandId);
            }
        }

        if (replicationIndex > AtomicCommandCommit.AtomicCommandCommitInitialSequence)
        {
            transaction.PutRecord(KeySpace.AppliedBytes, replicationIndex);
            if (persistOutcome)
            {
                // The last canonical outcome key lets another writer complete a one-position copy gap.
                transaction.PutRecord(KeySpace.LastCommandBytes, resultKey);
            }
        }

        if (!persistOutcome || replicationIndex <= AtomicCommandCommit.AtomicCommandCommitInitialSequence)
        {
            return;
        }

        if (transaction.ReadOwnedValue(KeySpace.ClockBytes) is not { } clock || operation.Timestamp >= OrleansSerialization.Deserialize<DateTimeOffset>(clock))
        {
            transaction.PutRecord(KeySpace.ClockBytes, (DateTimeOffset)operation.Timestamp);
        }
    }

    internal static Result<object> ValidateCompiledCommand(AtomicPartitionRosterTransaction transaction, Command<DatabaseCommand> operation, byte[] resultKey, StoredOutcome outcome, long replicationIndex, bool persistOutcome)
    {
        try
        {
            transaction.PersistCandidates();
            transaction.ValidateCommit();
            return outcome.Result;
        }
        catch (KeyLoadException exception) when (exception.Code == ErrorCode.ResourceExhausted)
        {
            // Indexes, images and outbox can expand a small request beyond its frame.
            // Reject at this same apply position without leaving an unapplicable entry.
            transaction.Reset();
            var failure = ManagedCode.Communication.Result<object>.Fail(Errors.Problem(exception.Code, exception.Message));
            var actualFailure = outcome with
            {
                Result = failure,
                CompositionAuthority = null,
                RemoteTransferFailureAuthority = null
            };
            CommandOutcomeApplication.PersistCommandOutcome(transaction, operation, resultKey, actualFailure, replicationIndex, persistOutcome);
            transaction.PersistCandidates();
            transaction.ValidateCommit();
            return failure;
        }
    }
}
