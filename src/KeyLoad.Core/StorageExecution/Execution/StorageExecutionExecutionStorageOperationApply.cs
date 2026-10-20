using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes StorageExecutionExecutionStorageOperationApply operations against the caller's database context.</summary>
public static class StorageExecutionExecutionStorageOperationApply
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
    public static Result<object> Apply(this DatabaseEngine engine, Command<DatabaseCommand> operation)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.ApplyCore(operation, null, selectEmbeddedClock: false, cancellationToken: default).Result;
    }

    /// <summary>Applies the next canonical sequence selected by the execution Orleans database grain.</summary>
    /// <param name = "operation">The verified command shared by the storage copies.</param>
    /// <param name = "sequence">The next canonical sequence; retries return the original stored sequence.</param>
    /// <param name = "cancellationToken">Bounds the actual storage commit.</param>
    /// <returns>The original sequence and freshly authorized command result.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static StorageOperationResult ApplyStorageOperation(this DatabaseEngine engine, Command<DatabaseCommand> operation, long sequence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        var verified = engine.OwnCommand(operation);
        if (engine.IsSiloLocalOperation(verified))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidStorageOperationScope);
        }

        return engine.ApplyCore(verified, sequence, selectEmbeddedClock: false, cancellationToken);
    }

    /// <summary>Applies node-bound derived work without advancing the canonical database read cut.</summary>
    /// <param name = "operation">The original verified node-bound operation.</param>
    /// <param name = "cancellationToken">Bounds the actual storage commit.</param>
    /// <returns>The actual local command result.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static Result<object> ApplyLocalStorageOperation(this DatabaseEngine engine, Command<DatabaseCommand> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var verified = engine.OwnCommand(operation);
        if (!engine.IsSiloLocalOperation(verified))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidStorageOperationScope);
        }

        return engine.ApplyCore(verified, null, selectEmbeddedClock: false, cancellationToken).Result;
    }

    internal static Result<object> ApplyEmbedded(this DatabaseEngine engine, Command<DatabaseCommand> operation, CancellationToken cancellationToken) => engine.ApplyCore(operation, null, selectEmbeddedClock: true, cancellationToken).Result;
    internal static long ReadCanonicalSequence(IKeyValueView view)
    {
        var encoded = view.ReadOwnedValue(KeySpace.AppliedBytes);
        var current = encoded is null ? InitialCanonicalSequence : OrleansSerialization.Deserialize<long>(encoded);
        if (current < InitialCanonicalSequence)
        {
            throw Errors.Fail(ErrorCode.Corruption, CanonicalSequenceRequired);
        }

        return current;
    }

    internal static long SelectNextCanonicalSequence(IKeyValueView view)
    {
        var current = ReadCanonicalSequence(view);
        if (current == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, CanonicalSequenceExhausted);
        }

        return current + NextCanonicalSequence;
    }

    internal static void RequireNextCanonicalSequence(IKeyValueView view, long sequence)
    {
        if (sequence != SelectNextCanonicalSequence(view))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, CanonicalSequenceRequired);
        }
    }

    internal static void RequireStoredCanonicalSequence(IKeyValueView view, StoredOutcome outcome, bool siloLocal)
    {
        if (siloLocal ? outcome.CanonicalSequence != InitialCanonicalSequence : outcome.CanonicalSequence <= InitialCanonicalSequence || outcome.CanonicalSequence > ReadCanonicalSequence(view))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, CanonicalSequenceRequired);
        }
    }
}
