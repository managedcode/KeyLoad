using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string CanonicalSequenceRequired = "The canonical storage sequence is not the next committed sequence.";
    private const string CanonicalSequenceExhausted = "The canonical storage sequence is exhausted.";
    private const string InvalidStorageOperationScope = "The operation requires its actual canonical or silo-local storage scope.";
    private const long InitialCanonicalSequence = 0;
    private const long NextCanonicalSequence = 1;

    /// <summary>Atomically applies one stable command or returns its freshly authorized persisted outcome.</summary>
    /// <param name="operation">The original command and evaluated business time.</param>
    /// <returns>The actual persisted command result.</returns>
    public OperationResult Apply(ReplicatedOperation operation)
        => ApplyCore(operation, null, selectEmbeddedClock: false, cancellationToken: default).Result;

    /// <summary>Applies the next canonical sequence selected by the native Orleans database grain.</summary>
    /// <param name="operation">The verified command shared by the storage copies.</param>
    /// <param name="sequence">The next canonical sequence; retries return the original stored sequence.</param>
    /// <param name="cancellationToken">Bounds the actual storage commit.</param>
    /// <returns>The original sequence and freshly authorized command result.</returns>
    public StorageOperationResult ApplyStorageOperation(ReplicatedOperation operation, long sequence,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        var verified = VerifyOperationAuthority(operation);
        if (IsSiloLocalOperation(verified))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidStorageOperationScope);
        }
        return ApplyCore(verified, sequence, selectEmbeddedClock: false, cancellationToken);
    }

    /// <summary>Applies node-bound derived work without advancing the canonical database read cut.</summary>
    /// <param name="operation">The original verified node-bound operation.</param>
    /// <param name="cancellationToken">Bounds the actual storage commit.</param>
    /// <returns>The actual local command result.</returns>
    public OperationResult ApplyLocalStorageOperation(ReplicatedOperation operation, CancellationToken cancellationToken)
    {
        var verified = VerifyOperationAuthority(operation);
        if (!IsSiloLocalOperation(verified))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidStorageOperationScope);
        }
        return ApplyCore(verified, null, selectEmbeddedClock: false, cancellationToken).Result;
    }

    internal OperationResult ApplyEmbedded(ReplicatedOperation operation, CancellationToken cancellationToken)
        => ApplyCore(operation, null, selectEmbeddedClock: true, cancellationToken).Result;

    private static long ReadCanonicalSequence(IKeyValueView view)
    {
        var encoded = view.ReadOwnedValue(KeySpace.AppliedBytes);
        var current = encoded is null ? InitialCanonicalSequence : NativeSerialization.Deserialize<long>(encoded);
        if (current < InitialCanonicalSequence)
        {
            throw Errors.Fail(ErrorCode.Corruption, CanonicalSequenceRequired);
        }
        return current;
    }

    private static long SelectNextCanonicalSequence(IKeyValueView view)
    {
        var current = ReadCanonicalSequence(view);
        if (current == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, CanonicalSequenceExhausted);
        }
        return current + NextCanonicalSequence;
    }

    private static void RequireNextCanonicalSequence(IKeyValueView view, long sequence)
    {
        if (sequence != SelectNextCanonicalSequence(view))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, CanonicalSequenceRequired);
        }
    }

    private static void RequireStoredCanonicalSequence(IKeyValueView view, StoredOutcome outcome, bool siloLocal)
    {
        if (siloLocal ? outcome.CanonicalSequence != InitialCanonicalSequence
            : outcome.CanonicalSequence <= InitialCanonicalSequence || outcome.CanonicalSequence > ReadCanonicalSequence(view))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, CanonicalSequenceRequired);
        }
    }
}
