using ZoneTree.Transactional;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Publishes one compiled commit as a single ZoneTree transaction; ZoneTree's write-ahead log records it.</summary>
internal static class ZoneTreeCommitPublication
{
    private const int FirstMutationIndex = 0;

    /// <summary>Stages mutations and position in the same native transaction. A failed physical publication
    /// leaves an unknown outcome, so the store refuses work until it is reopened.</summary>
    internal static void Stage(ZoneTreeStoreRuntime runtime, ITransaction<Memory<byte>, Memory<byte>> transaction,
        StorageMutation[] changes, long nextPosition)
    {
        try
        {
            for (var i = FirstMutationIndex; i < changes.Length; i++)
            {
                ZoneTreeStoreRuntime.Apply(transaction, changes[i]);
                runtime.Options.FaultObserver?.Invoke(CommitStage.MutationApplied, nextPosition, i);
            }

            ZoneTreeStorePosition.Stage(transaction, nextPosition);
        }
        catch (Exception original)
        {
            runtime.Poisoned = true;
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, UnknownWriteOutcome, original);
        }
    }

    internal static bool TryCommit(ZoneTreeStoreRuntime runtime, ITransaction<Memory<byte>, Memory<byte>> transaction)
    {
        try
        {
            return transaction.TryCommit();
        }
        catch (Exception original)
        {
            runtime.Poisoned = true;
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, UnknownWriteOutcome, original);
        }
    }

    internal static void Committed(ZoneTreeStoreRuntime runtime, long position, int mutations)
    {
        runtime.Operation.Value?.Published(position);
        runtime.Options.FaultObserver?.Invoke(CommitStage.ApplyCompleted, position, mutations);
    }
}
