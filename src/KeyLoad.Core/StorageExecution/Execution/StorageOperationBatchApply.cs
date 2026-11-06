using System.Collections.Immutable;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Core;

/// <summary>Applies consecutive canonical commands in one native process-persistent atomic storage commit.</summary>
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
        var verified = Verify(engine, operations, firstSequence);
        cancellationToken.ThrowIfCancellationRequested();
        return engine.Store.Commit((transaction, position)
            => engine.ApplyInOrder(transaction, position, verified, firstSequence, cancellationToken));
    }

    /// <summary>Applies ordered commands, publishing process-persistent plain data before their transactional outcomes.</summary>
    /// <param name="engine">The shared execution context.</param>
    /// <param name="operations">Prepared canonical commands.</param>
    /// <param name="firstSequence">The first newly persisted canonical sequence.</param>
    /// <param name="cancellationToken">Cancellation before physical publication.</param>
    /// <param name="retainReplication">Atomically retains the primary effects when other persisted copies are required.</param>
    /// <returns>The applied results after process-persistent native WAL publication.</returns>
    public static Task<StorageBatchResult> ApplyStorageOperationsAsync(this DatabaseEngine engine,
        IReadOnlyList<Command<DatabaseCommand>> operations, long firstSequence, CancellationToken cancellationToken,
        bool retainReplication = false)
    {
        var verified = Verify(engine, operations, firstSequence);
        var batchId = Guid.NewGuid();
        return engine.Store.CommitAsync((transaction, position)
            => engine.ApplyInOrder(transaction, position, verified, firstSequence, cancellationToken, retainReplication, batchId), cancellationToken);
    }

    private static Command<DatabaseCommand>[] Verify(DatabaseEngine engine,
        IReadOnlyList<Command<DatabaseCommand>> operations, long firstSequence)
    {
        ArgumentNullException.ThrowIfNull(engine) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(operations) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(firstSequence);
        if (operations.Count == 0)
        {
            throw new ArgumentException(EmptyBatch, nameof(operations));
        }

        var verified = new Command<DatabaseCommand>[operations.Count];
        for (var index = 0; index < verified.Length; index++)
        {
            verified[index] = engine.OwnCommand(operations[index]);
            if (engine.IsOwnedSiloLocalOperation(verified[index])
                || StorageCommandKeys.PartitionOf(verified[index]) != StorageCommandKeys.PartitionOf(verified[0]))
            {
                throw Errors.Fail(ErrorCode.Validation, StorageOperationApply.InvalidStorageOperationScope);
            }
        }

        return verified;
    }

    private static StorageBatchResult ApplyInOrder(this DatabaseEngine engine, IAtomicTransaction transaction,
        long position, Command<DatabaseCommand>[] operations, long firstSequence, CancellationToken cancellationToken,
        bool retainReplication = false, Guid batchId = default)
    {
        var partition = StorageCommandKeys.PartitionOf(operations[0]);
        var before = StorageOperationApply.ReadCanonicalSequence(transaction, partition);
        var staging = operations.Length == SingleCommand ? null
            : transaction as IAtomicActionStaging ?? throw new InvalidOperationException(ActionStagingRequired);
        var applied = ImmutableArray.CreateBuilder<StorageOperationResult>(operations.Length);
        var readPartitions = retainReplication ? new HashSet<PartitionRef>() : null;
        var current = before;
        var next = firstSequence;
        foreach (var operation in operations)
        {
            staging?.BeginAction();
            (StorageOperationResult Result, bool Persisted) result;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = engine.ApplyCommittedCommand(transaction, operation, position, current, next, siloLocal: false,
                    cancellationToken, readPartitions);
            }
            catch (Exception error) when (applied.Count > 0 && CqrsRuntimeFailures.FindFatal(error) is null)
            {
                transaction.Reset();
                break;
            }

            applied.Add(result.Result);
            if (!result.Persisted)
            {
                continue;
            }

            current = result.Result.Sequence;
            if (next == long.MaxValue)
            {
                break;
            }
            next++;
        }

        var batch = new StorageBatchResult(current, applied.ToImmutable());
        return retainReplication && batch.Position > before
            ? engine.RetainReplicaBatch(transaction, partition, before, batchId, batch, readPartitions) : batch;
    }

}
