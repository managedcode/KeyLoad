using System.Text;
using System.Text.Json;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string InvalidCommandBudgetMessage = "The operation ID or byte budget is invalid.";
    private const string PreviousCommandIncarnationMessage = "This command belongs to a previous database incarnation.";
    private const string CommandContentConflictMessage = "The command ID was already used with different content.";
    private const string CommittedClockAheadMessage = "The leader clock is behind the committed clock.";
    private const string InvalidCommandJsonMessage = "The operation contains invalid protocol JSON.";

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
        var resultKey = KeySpace.Outcome(operation.PrincipalId, operation.Id);
        if (replicationIndex > 0 && transaction.ReadOwnedValue(KeySpace.AppliedBytes) is { } appliedBytes
            && NativeSerialization.Deserialize<long>(appliedBytes) >= replicationIndex)
        {
            return transaction.GetRecord<StoredOutcome>(resultKey)?.Result ?? new(null);
        }
        var fingerprint = CommandFingerprint(operation);
        long policyEpoch = 0;
        BlobOutcomeAuthority? blobAuthority = null;
        OperationResult result;
        global::KeyLoad.Core.Features.DatabaseComposition.CompositionOutcomeAuthority? compositionAuthority = null;
        try
        {
            var principal = Principal(transaction, operation.PrincipalId, operation.EvaluatedAt);
            policyEpoch = principal.PolicyEpoch;
            AuthorizeOperation(transaction, principal, operation);
            if (transaction.GetRecord<StoredOutcome>(resultKey) is { } previous)
            {
                return ReplayCommand(transaction, principal, operation, previous, fingerprint, replicationIndex);
            }
            ValidateCommandClock(transaction, operation.EvaluatedAt);
            if (BlobStorageOperations.Handles(operation.Kind))
            {
                blobAuthority = new BlobStorageOperations(this).CaptureOutcomeAuthority(transaction, principal, operation);
            }
            result = Execute(transaction, principal, operation, replicationIndex > 0 ? replicationIndex : position);
            compositionAuthority = CaptureCompositionOutcome(operation, result);
        }
        catch (KeyLoadException exception) when (exception.Code is not (ErrorCode.Corruption or ErrorCode.FormatUnsupported
            or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome))
        {
            transaction.Reset();
            result = new(null, exception.Code, exception.Message);
        }
        catch (JsonException)
        {
            transaction.Reset();
            result = new(null, ErrorCode.Validation, InvalidCommandJsonMessage);
        }
        var outcome = new StoredOutcome(fingerprint, Store.Identity.Incarnation, policyEpoch, result)
        {
            BlobAuthority = result.Error is null ? blobAuthority : null,
            CompositionAuthority = result.Error is null ? compositionAuthority : null
        };
        PersistCommandOutcome(transaction, operation, resultKey, outcome, replicationIndex);
        return ValidateCompiledCommand(transaction, operation, resultKey, outcome, replicationIndex);
    }

    private OperationResult ReplayCommand(IAtomicTransaction transaction, PrincipalRecord principal,
        ReplicatedOperation operation, StoredOutcome previous, string fingerprint, long replicationIndex)
    {
        if (previous.Incarnation != Store.Identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, PreviousCommandIncarnationMessage);
        }
        if (previous.Fingerprint != fingerprint)
        {
            throw Errors.Fail(ErrorCode.Conflict, CommandContentConflictMessage);
        }
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
        StoredOutcome outcome, long replicationIndex)
    {
        // Domain effects, outcome and apply watermark share one redo transaction.
        if (transaction.ReadOwnedValue(resultKey) is null)
        {
            transaction.PutRecord(resultKey, outcome);
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
        byte[] resultKey, StoredOutcome outcome, long replicationIndex)
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
            { Result = failure, BlobAuthority = null, CompositionAuthority = null }, replicationIndex);
            transaction.ValidateCommit();
            return failure;
        }
    }
}
