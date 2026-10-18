using System.Text;
using System.Text.Json;
using KeyLoad.Core.Features.BackupRestore.Execution;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int AtomicCommandCommitFirstElementIndex = 0;
    private const int AtomicCommandCommitInitialSequence = 0;

    private const string InvalidCommandBudgetMessage = "The operation ID or byte budget is invalid.";
    private const string PreviousCommandIncarnationMessage = "This command belongs to a previous database incarnation.";
    private const string CommandContentConflictMessage = "The command ID was already used with different content.";
    private const string CommittedClockAheadMessage = "The operation time is behind the committed storage time.";
    private const string InvalidCommandJsonMessage = "The operation contains invalid protocol JSON.";
    private const string MissingOutcomePartitionMessage = "A partition-scoped command outcome has no partition identity.";

    /// <summary>Prepares one authorized operation once before sending identical bytes to storage copies.</summary>
    public ReplicatedOperation PrepareStorageOperation(ReplicatedOperation operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var verified = VerifyOperationAuthority(operation);
        var prepared = Store.Read(view =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = EvaluationClock.GetUtcNow();
            if (view.ReadOwnedValue(KeySpace.ClockBytes) is { } encoded)
            {
                var committed = NativeSerialization.Deserialize<DateTimeOffset>(encoded);
                now = now < committed ? committed : now;
            }
            return PrepareQueueRetryInView(view, verified with { EvaluatedAt = now });
        });
        return prepared;
    }

    private StorageOperationResult ApplyCore(ReplicatedOperation operation, long? canonicalSequence,
        bool selectEmbeddedClock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Id == Guid.Empty || Encoding.UTF8.GetByteCount(operation.PayloadJson) > Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidCommandBudgetMessage);
        }
        operation = NormalizeOperation(operation);
        var siloLocal = IsSiloLocalOperation(operation);
        var committed = Store.Commit((transaction, position) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var admitted = selectEmbeddedClock ? operation with
            {
                EvaluatedAt = EvaluationClock.GetUtcNow()
            } : operation;
            if (selectEmbeddedClock)
            {
                admitted = PrepareQueueRetryInView(transaction, admitted);
            }
            var sequence = siloLocal ? InitialCanonicalSequence
                : canonicalSequence ?? SelectNextCanonicalSequence(transaction);
            return ApplyCommittedCommand(transaction, admitted, position, sequence, siloLocal);
        });
        return committed;
    }

    private StorageOperationResult ApplyCommittedCommand(IAtomicTransaction transaction, ReplicatedOperation operation,
        long position, long replicationIndex, bool siloLocal)
    {
        var rosterTransaction = new AtomicPartitionRosterTransaction(transaction, OperationLimitsOptions, position, replicationIndex);
        var partitionScope = CommandOutcomePartitionIdentity.Resolve(operation);
        var fingerprint = CommandFingerprint(operation);
        var outcome = ExecuteAndBuildOutcome(rosterTransaction, operation, position, replicationIndex, fingerprint,
            partitionScope, siloLocal, out var replayed, out var selection, out var persistOutcome);
        if (replayed || !persistOutcome)
        {
            return new(outcome.CanonicalSequence, outcome.Result);
        }
        outcome = outcome with { CanonicalSequence = replicationIndex };
        PersistCommandOutcome(rosterTransaction, operation, selection.Key, outcome, replicationIndex, persistOutcome);
        var result = ValidateCompiledCommand(rosterTransaction, operation, selection.Key, outcome, replicationIndex,
            persistOutcome, Store.Identity.Incarnation);
        return new(outcome.CanonicalSequence, result);
    }

    private StoredOutcome ExecuteAndBuildOutcome(AtomicPartitionRosterTransaction transaction, ReplicatedOperation operation,
        long position, long replicationIndex, string fingerprint, CommandOutcomePartitionScope partitionScope,
        bool siloLocal, out bool replayed, out CommandOutcomeSelection selection, out bool persistOutcome)
    {
        replayed = false;
        selection = CommandOutcomeKeyResolver.ForNew(operation.PrincipalId, operation.Id, partitionScope);
        persistOutcome = true;
        long policyEpoch = AtomicCommandCommitInitialSequence;
        BlobOutcomeAuthority? blobAuthority = null;
        var onlineTextAuthority = CaptureOnlineTextOutcomeAuthority(operation);
        global::KeyLoad.Core.Features.Messaging.RemoteTransferAcceptFailureAuthority? transferFailure = null;
        OperationResult result;
        global::KeyLoad.Core.Features.DatabaseComposition.CompositionOutcomeAuthority? compositionAuthority = null;
        try
        {
            var principal = Principal(transaction, operation.PrincipalId, operation.EvaluatedAt);
            policyEpoch = principal.PolicyEpoch;
            var placement = AuthorizeOperation(transaction, principal, operation);
            selection = CommandOutcomeKeyResolver.Select(transaction, operation.PrincipalId, operation.Id, partitionScope);
            if (selection.Outcome is { } previous)
            {
                RequireStoredCanonicalSequence(transaction, previous, siloLocal);
                result = ReplayCommand(transaction, principal, operation, previous, fingerprint, selection);
                replayed = true;
                return previous;
            }
            if (!siloLocal)
            {
                RequireNextCanonicalSequence(transaction, replicationIndex);
            }
            RequireOriginalEffectAdmission(transaction, principal, operation);
            ValidateCommandClock(transaction, operation.EvaluatedAt);
            ValidateQueueRetryDecisions(transaction, principal, operation);
            blobAuthority = CaptureBlobOutcomeAuthority(transaction, principal, operation);
            transferFailure = CaptureRemoteTransferFailureAuthority(transaction, principal, operation);
            result = Execute(transaction, principal, operation, replicationIndex > AtomicCommandCommitInitialSequence ? replicationIndex : position,
                placement);
            compositionAuthority = CaptureCompositionOutcome(transaction, principal, operation, result);
        }
        catch (KeyLoadException exception) when (exception.Code is not (ErrorCode.Corruption or ErrorCode.FormatUnsupported
            or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome))
        {
            transaction.Reset();
            replayed = false;
            result = new(null, exception.Code, exception.Message);
        }
        catch (JsonException)
        {
            transaction.Reset();
            replayed = false;
            result = new(null, ErrorCode.Validation, InvalidCommandJsonMessage);
        }
        persistOutcome = selection.Outcome is null;
        var actualOutcome = BuildStoredOutcome(fingerprint, policyEpoch, result, blobAuthority, compositionAuthority,
            partitionScope, onlineTextAuthority, transferFailure) with
        {
            CanonicalSequence = selection.Outcome?.CanonicalSequence ?? replicationIndex
        };
        return actualOutcome;
    }

    private BlobOutcomeAuthority? CaptureBlobOutcomeAuthority(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation)
        => BlobStorageOperations.Handles(operation.Kind)
            ? new BlobStorageOperations(this).CaptureOutcomeAuthority(transaction, principal, operation) : null;

    private StoredOutcome BuildStoredOutcome(string fingerprint, long policyEpoch, OperationResult result,
        BlobOutcomeAuthority? blobAuthority,
        global::KeyLoad.Core.Features.DatabaseComposition.CompositionOutcomeAuthority? compositionAuthority,
        CommandOutcomePartitionScope partitionScope,
        global::KeyLoad.Core.Features.Search.OnlineTextOutcomeAuthority? onlineTextAuthority,
        global::KeyLoad.Core.Features.Messaging.RemoteTransferAcceptFailureAuthority? transferFailure)
        => new(fingerprint, Store.Identity.Incarnation, policyEpoch, result)
        {
            BlobAuthority = result.Error is null ? blobAuthority : null,
            CompositionAuthority = result.Error is null ? compositionAuthority : null,
            ScopeKind = partitionScope.Kind,
            Partition = partitionScope.Partition,
            OnlineTextAuthority = onlineTextAuthority,
            RemoteTransferFailureAuthority = HasRemoteTransferCapacityFailure(result) ? transferFailure : null
        };

    private OperationResult ReplayCommand(IAtomicTransaction transaction, PrincipalRecord principal,
        ReplicatedOperation operation, StoredOutcome previous, string fingerprint,
        CommandOutcomeSelection selection)
    {
        if (previous.Incarnation != Store.Identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, PreviousCommandIncarnationMessage);
        }
        if (previous.Fingerprint != fingerprint)
        {
            throw Errors.Fail(ErrorCode.Conflict, CommandContentConflictMessage);
        }
        CommandOutcomeKeyResolver.ValidateSelectedScope(transaction, operation, selection);
        var current = operation with { EvaluatedAt = Clock.GetUtcNow() };
        principal = Principal(transaction, operation.PrincipalId, current.EvaluatedAt);
        AuthorizeOperation(transaction, principal, current);
        ValidateCachedResult(transaction, principal, current, previous);
        return previous.Result;
    }

    private static void ValidateCommandClock(AtomicPartitionRosterTransaction view, DateTimeOffset evaluatedAt)
    {
        if (view.ReadOwnedValue(KeySpace.ClockBytes) is { } clock && evaluatedAt < NativeSerialization.Deserialize<DateTimeOffset>(clock))
        {
            throw Errors.Fail(ErrorCode.ClockUncertain, CommittedClockAheadMessage);
        }
    }

    private static void PersistCommandOutcome(AtomicPartitionRosterTransaction transaction, ReplicatedOperation operation, byte[] resultKey,
        StoredOutcome outcome, long replicationIndex, bool persistOutcome)
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
                    throw Errors.Fail(ErrorCode.Corruption, MissingOutcomePartitionMessage);
                }
                CommandOutcomePartitionLocatorSerialization.WriteScoped(transaction, outcome.Partition,
                    operation.PrincipalId, operation.Id);
            }
        }
        if (replicationIndex > AtomicCommandCommitInitialSequence)
        {
            transaction.PutRecord(KeySpace.AppliedBytes, replicationIndex);
        }
        if (!persistOutcome || outcomeAlreadyStored && replicationIndex <= AtomicCommandCommitInitialSequence)
        {
            return;
        }
        if (transaction.ReadOwnedValue(KeySpace.ClockBytes) is not { } clock
            || operation.EvaluatedAt >= NativeSerialization.Deserialize<DateTimeOffset>(clock))
        {
            transaction.PutRecord(KeySpace.ClockBytes, operation.EvaluatedAt);
        }
    }

    private static OperationResult ValidateCompiledCommand(AtomicPartitionRosterTransaction transaction, ReplicatedOperation operation,
        byte[] resultKey, StoredOutcome outcome, long replicationIndex, bool persistOutcome, Guid currentIncarnation)
    {
        try
        {
            transaction.PersistCandidates(currentIncarnation);
            transaction.ValidateCommit();
            return outcome.Result;
        }
        catch (KeyLoadException exception) when (exception.Code == ErrorCode.ResourceExhausted)
        {
            // Indexes, images and outbox can expand a small request beyond its frame.
            // Reject at this same apply position without leaving an unapplicable entry.
            transaction.Reset();
            var failure = new OperationResult(null, exception.Code, exception.Message);
            var actualFailure = outcome with
            {
                Result = failure,
                BlobAuthority = null,
                CompositionAuthority = null,
                RemoteTransferFailureAuthority = null
            };
            PersistCommandOutcome(transaction, operation, resultKey, actualFailure, replicationIndex, persistOutcome);
            transaction.PersistCandidates(currentIncarnation);
            transaction.ValidateCommit();
            return failure;
        }
    }
}
