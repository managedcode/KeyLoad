using System.Text;
using System.Text.Json;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
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
    public OperationResult Apply(ReplicatedOperation operation, long replicationIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Id == Guid.Empty || Encoding.UTF8.GetByteCount(operation.PayloadJson) > Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidCommandBudgetMessage);
        }
        operation = NormalizeOperation(operation);
        return Store.Commit((transaction, position) => ApplyCommittedCommand(transaction, operation, position, replicationIndex));
    }

    private OperationResult ApplyCommittedCommand(IAtomicTransaction transaction, ReplicatedOperation operation,
        long position, long replicationIndex)
    {
        var partitionScope = CommandOutcomePartitionIdentity.Resolve(operation);
        if (ReadAlreadyAppliedOutcome(transaction, operation, replicationIndex, partitionScope) is { } applied)
        {
            return applied;
        }
        var fingerprint = CommandFingerprint(operation);
        var outcome = ExecuteAndBuildOutcome(transaction, operation, position, replicationIndex, fingerprint,
            partitionScope, out var replayed, out var selection, out var persistOutcome);
        if (replayed)
        {
            return outcome.Result;
        }
        PersistCommandOutcome(transaction, operation, selection.Key, outcome, replicationIndex, persistOutcome);
        return ValidateCompiledCommand(transaction, operation, selection.Key, outcome, replicationIndex, persistOutcome);
    }

    private static OperationResult? ReadAlreadyAppliedOutcome(IAtomicTransaction transaction, ReplicatedOperation operation,
        long replicationIndex, CommandOutcomePartitionScope scope)
    {
        if (replicationIndex <= 0 || transaction.ReadOwnedValue(KeySpace.AppliedBytes) is not { } appliedBytes
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

    private StoredOutcome ExecuteAndBuildOutcome(IAtomicTransaction transaction, ReplicatedOperation operation,
        long position, long replicationIndex, string fingerprint, CommandOutcomePartitionScope partitionScope,
        out bool replayed, out CommandOutcomeSelection selection, out bool persistOutcome)
    {
        replayed = false;
        var authorized = false;
        selection = CommandOutcomeKeyResolver.ForNew(operation.PrincipalId, operation.Id, partitionScope);
        persistOutcome = true;
        long policyEpoch = 0;
        BlobOutcomeAuthority? blobAuthority = null;
        OperationResult result;
        global::KeyLoad.Core.Features.DatabaseComposition.CompositionOutcomeAuthority? compositionAuthority = null;
        try
        {
            var principal = Principal(transaction, operation.PrincipalId, operation.EvaluatedAt);
            policyEpoch = principal.PolicyEpoch;
            var placement = AuthorizeOperation(transaction, principal, operation);
            selection = CommandOutcomeKeyResolver.Select(transaction, operation.PrincipalId, operation.Id, partitionScope);
            authorized = true;
            if (selection.Outcome is { } previous)
            {
                result = ReplayCommand(transaction, principal, operation, previous, fingerprint, replicationIndex, selection);
                replayed = true;
                return previous;
            }
            else
            {
                ValidateCommandClock(transaction, operation.EvaluatedAt);
                if (BlobStorageOperations.Handles(operation.Kind))
                {
                    blobAuthority = new BlobStorageOperations(this).CaptureOutcomeAuthority(transaction, principal, operation);
                }
                result = Execute(transaction, principal, operation, replicationIndex > 0 ? replicationIndex : position,
                    placement);
                compositionAuthority = CaptureCompositionOutcome(operation, result);
            }
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
        if (!authorized && CommandOutcomeKeyResolver.HasLegacyOutcome(transaction, operation.PrincipalId, operation.Id))
        {
            persistOutcome = false;
        }
        return BuildStoredOutcome(fingerprint, policyEpoch, result, blobAuthority, compositionAuthority, partitionScope);
    }

    private StoredOutcome BuildStoredOutcome(string fingerprint, long policyEpoch, OperationResult result,
        BlobOutcomeAuthority? blobAuthority,
        global::KeyLoad.Core.Features.DatabaseComposition.CompositionOutcomeAuthority? compositionAuthority,
        CommandOutcomePartitionScope partitionScope)
        => new(fingerprint, Store.Identity.Incarnation, policyEpoch, result)
        {
            BlobAuthority = result.Error is null ? blobAuthority : null,
            CompositionAuthority = result.Error is null ? compositionAuthority : null,
            ScopeKind = partitionScope.Kind,
            Partition = partitionScope.Partition
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
        if (replicationIndex > 0)
        {
            transaction.PutRecord(KeySpace.AppliedBytes, replicationIndex);
        }
        return previous.Result;
    }

    private static void ValidateCommandClock(IKeyValueView view, DateTimeOffset evaluatedAt)
    {
        if (view.ReadOwnedValue(KeySpace.ClockBytes) is { } clock && evaluatedAt < NativeSerialization.Deserialize<DateTimeOffset>(clock))
        {
            throw Errors.Fail(ErrorCode.ClockUncertain, CommittedClockAheadMessage);
        }
    }

    private static void PersistCommandOutcome(IAtomicTransaction transaction, ReplicatedOperation operation, byte[] resultKey,
        StoredOutcome outcome, long replicationIndex, bool persistOutcome)
    {
        // Domain effects, outcome and apply watermark share one redo transaction.
        if (persistOutcome && transaction.ReadOwnedValue(resultKey) is null)
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
        if (replicationIndex > 0)
        {
            transaction.PutRecord(KeySpace.AppliedBytes, replicationIndex);
        }
        if (transaction.ReadOwnedValue(KeySpace.ClockBytes) is not { } clock
            || operation.EvaluatedAt >= NativeSerialization.Deserialize<DateTimeOffset>(clock))
        {
            transaction.PutRecord(KeySpace.ClockBytes, operation.EvaluatedAt);
        }
    }

    private static OperationResult ValidateCompiledCommand(IAtomicTransaction transaction, ReplicatedOperation operation,
        byte[] resultKey, StoredOutcome outcome, long replicationIndex, bool persistOutcome)
    {
        try
        {
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
            { Result = failure, BlobAuthority = null, CompositionAuthority = null }, replicationIndex, persistOutcome);
            transaction.ValidateCommit();
            return failure;
        }
    }
}
