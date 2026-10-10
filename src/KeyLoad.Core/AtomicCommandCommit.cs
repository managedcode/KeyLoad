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
    private const string CommittedClockAheadMessage = "The leader clock is behind the committed clock.";
    private const string InvalidCommandJsonMessage = "The operation contains invalid protocol JSON.";
    private const string MissingOutcomePartitionMessage = "A partition-scoped command outcome has no partition identity.";

    /// <summary>Atomically applies one stable command identity or returns its persisted outcome.</summary>
    /// <param name="operation">Authenticated operation with its evaluated business time.</param>
    /// <param name="replicationIndex">Optional committed replica position.</param>
    /// <returns>The durable logical result, including a stored domain failure.</returns>
    public OperationResult Apply(ReplicatedOperation operation, long replicationIndex = AtomicCommandCommitFirstElementIndex)
        => ApplyCore(operation, replicationIndex, selectEmbeddedClock: false, cancellationToken: default);

    internal OperationResult ApplyEmbedded(ReplicatedOperation operation, CancellationToken cancellationToken)
        => ApplyCore(operation, AtomicCommandCommitFirstElementIndex, selectEmbeddedClock: true, cancellationToken);

    private OperationResult ApplyCore(ReplicatedOperation operation, long replicationIndex,
        bool selectEmbeddedClock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Id == Guid.Empty || Encoding.UTF8.GetByteCount(operation.PayloadJson) > Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidCommandBudgetMessage);
        }
        if (operation.Kind == OperationKind.PartitionMovementPhase)
        { RequireConfiguredMovementOwner(); }
        operation = NormalizeOperation(operation);
        return Store.Commit((transaction, position) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var admitted = selectEmbeddedClock ? operation with { EvaluatedAt = EvaluationClock.GetUtcNow() } : operation;
            if (selectEmbeddedClock)
            { admitted = PrepareQueueRetryInView(transaction, admitted); }
            return ApplyCommittedCommand(transaction, admitted, position, replicationIndex);
        });
    }

    private OperationResult ApplyCommittedCommand(IAtomicTransaction transaction, ReplicatedOperation operation,
        long position, long replicationIndex)
    {
        var rosterTransaction = new AtomicPartitionRosterTransaction(transaction, OperationLimitsOptions, position, replicationIndex);
        var partitionScope = CommandOutcomePartitionIdentity.Resolve(operation);
        if (ReadAlreadyAppliedOutcome(rosterTransaction, operation, replicationIndex, partitionScope) is { } applied)
        {
            return applied;
        }
        var fingerprint = CommandFingerprint(operation);
        var outcome = ExecuteAndBuildOutcome(rosterTransaction, operation, position, replicationIndex, fingerprint,
            partitionScope, out var replayed, out var selection, out var persistOutcome);
        if (replayed)
        {
            return outcome.Result;
        }
        PersistCommandOutcome(rosterTransaction, operation, selection.Key, outcome, replicationIndex, persistOutcome);
        return ValidateCompiledCommand(rosterTransaction, operation, selection.Key, outcome, replicationIndex, persistOutcome, Store.Identity.Incarnation);
    }

    private static OperationResult? ReadAlreadyAppliedOutcome(AtomicPartitionRosterTransaction transaction, ReplicatedOperation operation,
        long replicationIndex, CommandOutcomePartitionScope scope)
    {
        if (replicationIndex <= AtomicCommandCommitInitialSequence || transaction.ReadOwnedValue(KeySpace.AppliedBytes) is not { } appliedBytes
            || NativeSerialization.Deserialize<long>(appliedBytes) < replicationIndex)
        {
            return null;
        }
        var selection = CommandOutcomeKeyResolver.Select(transaction, operation.PrincipalId, operation.Id, scope);
        if (selection.Outcome is not { } applied)
        {
            return new(null);
        }
        CommandOutcomeKeyResolver.ValidateSelectedScope(transaction, operation, selection);
        return applied.Result;
    }

    private StoredOutcome ExecuteAndBuildOutcome(AtomicPartitionRosterTransaction transaction, ReplicatedOperation operation,
        long position, long replicationIndex, string fingerprint, CommandOutcomePartitionScope partitionScope,
        out bool replayed, out CommandOutcomeSelection selection, out bool persistOutcome)
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
                result = ReplayCommand(transaction, principal, operation, previous, fingerprint, replicationIndex, selection);
                replayed = true;
                return previous;
            }
            RequireNoPartitionMovementFence(transaction, operation, partitionScope);
            ValidateCommandClock(transaction, operation.EvaluatedAt);
            ValidateQueueRetryDecisions(transaction, principal, operation);
            blobAuthority = CaptureBlobOutcomeAuthority(transaction, principal, operation);
            transferFailure = CaptureRemoteTransferFailureAuthority(transaction, principal, operation);
            result = Execute(transaction, principal, operation, replicationIndex > AtomicCommandCommitInitialSequence ? replicationIndex : position,
                placement);
            compositionAuthority = CaptureCompositionOutcome(operation, result);
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
        return BuildStoredOutcome(fingerprint, policyEpoch, result, blobAuthority, compositionAuthority, partitionScope, onlineTextAuthority) with
        { RemoteTransferFailureAuthority = HasRemoteTransferCapacityFailure(result) ? transferFailure : null };
    }

    private BlobOutcomeAuthority? CaptureBlobOutcomeAuthority(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation)
        => BlobStorageOperations.Handles(operation.Kind)
            ? new BlobStorageOperations(this).CaptureOutcomeAuthority(transaction, principal, operation) : null;

    private StoredOutcome BuildStoredOutcome(string fingerprint, long policyEpoch, OperationResult result,
        BlobOutcomeAuthority? blobAuthority,
        global::KeyLoad.Core.Features.DatabaseComposition.CompositionOutcomeAuthority? compositionAuthority,
        CommandOutcomePartitionScope partitionScope,
        global::KeyLoad.Core.Features.Search.OnlineTextOutcomeAuthority? onlineTextAuthority)
        => new(fingerprint, Store.Identity.Incarnation, policyEpoch, result)
        {
            BlobAuthority = result.Error is null ? blobAuthority : null,
            CompositionAuthority = result.Error is null ? compositionAuthority : null,
            ScopeKind = partitionScope.Kind,
            Partition = partitionScope.Partition,
            OnlineTextAuthority = onlineTextAuthority
        };

    private OperationResult ReplayCommand(IAtomicTransaction transaction, PrincipalRecord principal,
        ReplicatedOperation operation, StoredOutcome previous, string fingerprint, long replicationIndex,
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
        ValidateCachedResult(transaction, principal, operation, previous);
        if (replicationIndex > AtomicCommandCommitInitialSequence)
        {
            transaction.PutRecord(KeySpace.AppliedBytes, replicationIndex);
        }
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
            PersistCommandOutcome(transaction, operation, resultKey, outcome with
            { Result = failure, BlobAuthority = null, CompositionAuthority = null, RemoteTransferFailureAuthority = null }, replicationIndex, persistOutcome);
            transaction.PersistCandidates(currentIncarnation);
            transaction.ValidateCommit();
            return failure;
        }
    }
}
