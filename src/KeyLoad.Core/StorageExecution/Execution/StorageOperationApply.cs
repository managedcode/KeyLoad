using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes StorageOperationApply operations against the caller's database context.</summary>
public static class StorageOperationApply
{
    internal const string CanonicalSequenceRequired = "The canonical storage sequence is not the next committed sequence.";
    internal const string CanonicalSequenceExhausted = "The canonical storage sequence is exhausted.";
    internal const string InvalidStorageOperationScope = "The operation requires its actual canonical or silo-local storage scope.";
    internal const long InitialCanonicalSequence = 0;
    internal const long NextCanonicalSequence = 1;
    /// <summary>Atomically applies one stable command or returns its freshly authorized persisted outcome.</summary>
    /// <param name = "operation">The original command and evaluated business time.</param>
    /// <returns>The actual persisted command result.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static Result<object> /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */ Apply(this DatabaseEngine engine, Command<DatabaseCommand> operation)
    {
        ArgumentNullException.ThrowIfNull(engine) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        return engine.ApplyCore(operation, null, selectEmbeddedClock: false, cancellationToken: default).Result /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */;
    }

    /// <summary>Applies the next canonical sequence selected by the execution Orleans database grain.</summary>
    /// <param name = "operation">The verified command shared by the storage copies.</param>
    /// <param name = "sequence">The next canonical sequence; retries return the original stored sequence.</param>
    /// <param name = "cancellationToken">Bounds the actual storage commit.</param>
    /// <returns>The original sequence and freshly authorized command result.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static StorageOperationResult ApplyStorageOperation(this DatabaseEngine engine, Command<DatabaseCommand> operation, long sequence, CancellationToken cancellationToken)
        => engine.ApplyStorageOperations([operation], sequence, cancellationToken).Applied[0];

    /// <summary>Applies node-bound derived work without advancing the canonical database read cut.</summary>
    /// <param name = "operation">The original verified node-bound operation.</param>
    /// <param name = "cancellationToken">Bounds the actual storage commit.</param>
    /// <returns>The actual local command result.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static Result<object> /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */ ApplyLocalStorageOperation(this DatabaseEngine engine, Command<DatabaseCommand> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        var verified = engine.OwnCommand(operation);
        if (!engine.IsOwnedSiloLocalOperation(verified))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidStorageOperationScope);
        }

        return engine.ApplyCore(verified, null, selectEmbeddedClock: false, cancellationToken).Result /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */;
    }

    internal static Result<object> /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */ ApplyEmbedded(this DatabaseEngine engine, Command<DatabaseCommand> operation, CancellationToken cancellationToken) => engine.ApplyCore(operation, null, selectEmbeddedClock: true, cancellationToken).Result /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */;
    /// <summary>Reads one owner's sequence from the same native cut as the operation.</summary>
    public static long ReadCanonicalSequence(IKeyValueView view, PartitionRef? partition)
    {
        var encoded = view.GetRecord<StoragePartitionPosition>(StorageCommandKeys.Applied(partition));
        return encoded is null ? InitialCanonicalSequence : RequireCanonicalSequence(encoded, partition, null);
    }

    /// <summary>Validates a stored prefix against its actual key scope before using its position.</summary>
    public static long ReadCanonicalSequence(ReadOnlySpan<byte> bytes, PartitionRef? partition, string? invalidMessage = null)
    {
        var encoded = OrleansSerialization.Deserialize<StoragePartitionPosition>(bytes);
        return RequireCanonicalSequence(encoded, partition, invalidMessage);
    }

    private static long RequireCanonicalSequence(StoragePartitionPosition? encoded, PartitionRef? partition, string? invalidMessage)
    {
        if (encoded is null || encoded.Position < InitialCanonicalSequence || encoded.Partition != partition)
        {
            throw Errors.Fail(ErrorCode.Corruption, invalidMessage ?? CanonicalSequenceRequired);
        }

        return encoded.Position;
    }

    internal static long SelectNextCanonicalSequence(long current)
    {
        if (current == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, CanonicalSequenceExhausted);
        }

        return current + NextCanonicalSequence;
    }

    internal static void RequireNextCanonicalSequence(long current, long sequence)
    {
        if (sequence != SelectNextCanonicalSequence(current))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, CanonicalSequenceRequired);
        }
    }

    internal static void RequireStoredCanonicalSequence(IKeyValueView view, StoredOutcome outcome, bool siloLocal)
        => RequireStoredCanonicalSequence(ReadCanonicalSequence(view, StorageCommandKeys.PartitionOf(outcome.PreparedCommand)), outcome, siloLocal);

    internal static void RequireStoredCanonicalSequence(long current, StoredOutcome outcome, bool siloLocal)
    {
        if (siloLocal ? outcome.CanonicalSequence != InitialCanonicalSequence : outcome.CanonicalSequence <= InitialCanonicalSequence || outcome.CanonicalSequence > current)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, CanonicalSequenceRequired);
        }
    }
}
