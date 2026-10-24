using System.Collections.Immutable;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Core;

/// <summary>Applies consecutive canonical commands in one atomic storage commit with one durable journal flush.</summary>
public static class StorageOperationBatchApply
{
    internal const string EmptyBatch = "A storage batch requires at least one command.";
    internal const string ActionStagingRequired = "The atomic store must stage several actions in one commit.";
    private const int SingleCommand = 1;

    /// <summary>Applies the longest prefix of <paramref name="operations"/> that fits one commit, in order.</summary>
    /// <param name="engine">The shared database execution context.</param>
    /// <param name="operations">Prepared canonical commands in submission order.</param>
    /// <param name="firstSequence">The canonical sequence the first newly persisted outcome takes.</param>
    /// <param name="cancellationToken">Bounds the actual storage commit.</param>
    /// <returns>The applied prefix's results and the committed canonical sequence after it.</returns>
    /// <remarks>Each command sees the effects of the commands before it and takes the next sequence when it persists
    /// a new outcome; a retry of a committed command keeps its original sequence. A later command that does not fit
    /// the shared journal frame, or that fails outside its own persisted outcome, is not applied: it runs first in a
    /// later commit, exactly as in a commit of its own. The first command always runs.</remarks>
    public static StorageBatchResult ApplyStorageOperations(this DatabaseEngine engine,
        IReadOnlyList<Command<DatabaseCommand>> operations, long firstSequence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(firstSequence);
        if (operations.Count == 0)
        {
            throw new ArgumentException(EmptyBatch, nameof(operations));
        }

        var verified = new Command<DatabaseCommand>[operations.Count];
        for (var index = 0; index < verified.Length; index++)
        {
            verified[index] = engine.OwnCommand(operations[index]);
            if (engine.IsSiloLocalOperation(verified[index]))
            {
                throw Errors.Fail(ErrorCode.Validation, StorageOperationApply.InvalidStorageOperationScope);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return engine.Store.Commit((transaction, position)
            => engine.ApplyInOrder(transaction, position, verified, firstSequence, cancellationToken));
    }

    private static StorageBatchResult ApplyInOrder(this DatabaseEngine engine, IAtomicTransaction transaction,
        long position, Command<DatabaseCommand>[] operations, long firstSequence, CancellationToken cancellationToken)
    {
        var staging = operations.Length == SingleCommand ? null
            : transaction as IAtomicActionStaging ?? throw new InvalidOperationException(ActionStagingRequired);
        var applied = ImmutableArray.CreateBuilder<StorageOperationResult>(operations.Length);
        var outcomeKeys = new List<byte[]>(operations.Length);
        var next = firstSequence;
        foreach (var operation in operations)
        {
            staging?.BeginAction();
            StorageOperationResult result;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = engine.ApplyCommittedCommand(transaction, operation, position, next, siloLocal: false,
                    cancellationToken);
            }
            catch (Exception error) when (applied.Count > 0 && CqrsRuntimeFailures.FindFatal(error) is null)
            {
                transaction.Reset();
                break;
            }

            if (applied.Count > 0 && staging is { FitsOneFrame: false })
            {
                transaction.Reset();
                break;
            }

            applied.Add(result);
            if (StorageOperationApply.ReadCanonicalSequence(transaction) != next)
            {
                continue;
            }

            outcomeKeys.Add(transaction.GetRecord<byte[]>(KeySpace.LastCommandBytes)!);
            if (next == long.MaxValue)
            {
                break;
            }
            next++;
        }

        RetainBatchOutcomeKeys(transaction, outcomeKeys);
        return new(StorageOperationApply.ReadCanonicalSequence(transaction), applied.ToImmutable());
    }

    /// <summary>Records every outcome key of a commit that persisted several commands, so a later writer can
    /// complete a copy that missed the whole commit.</summary>
    private static void RetainBatchOutcomeKeys(IAtomicTransaction transaction, List<byte[]> outcomeKeys)
    {
        if (outcomeKeys.Count > SingleCommand)
        {
            transaction.PutRecord(KeySpace.LastBatchBytes, outcomeKeys.ToArray());
        }
    }
}
