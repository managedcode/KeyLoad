using System.Runtime.InteropServices;
using ZoneTree.Transactional;
using PlainValue = ZoneTree.PresetTypes.Deletable<System.Memory<byte>>;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Publishes plain Upserts into the native process-persistent WAL before the transactional outcome.</summary>
internal static class ZoneTreePlainPublication
{
    internal static void Publish(ZoneTreeStoreRuntime runtime, ITransaction<Memory<byte>, Memory<byte>> transaction,
        StorageMutation[] plain, StorageMutation[] metadata, long position)
    {
        try
        {
            Apply(runtime, plain, position);
            // Every native Upsert has reached the mapped WAL before the command outcome can be published.
            runtime.Options.FaultObserver?.Invoke(CommitStage.PlainWriteAheadLogPublished, position, plain.Length);
            ZoneTreeCommitPublication.Stage(runtime, transaction, metadata, position);
            transaction.Commit();
            ZoneTreeCommitPublication.Committed(runtime, position, plain.Length + metadata.Length);
        }
        catch (Exception original)
        {
            runtime.Poisoned = true;
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, ZoneTreePersistenceFormat.UnknownWriteOutcome, original);
        }
    }

    private static void Apply(ZoneTreeStoreRuntime runtime, StorageMutation[] changes, long position)
    {
        runtime.Check();
        for (var index = 0; index < changes.Length; index++)
        {
            var mutation = changes[index];
            var key = MemoryMarshal.AsMemory(mutation.Key);
            if (mutation.Value is { } bytes)
            {
                runtime.PlainTree.Upsert(key, new PlainValue(MemoryMarshal.AsMemory(bytes)));
            }
            else
            {
                runtime.PlainTree.ForceDelete(key);
            }
            runtime.Options.FaultObserver?.Invoke(CommitStage.MutationApplied, position, index);
        }
    }
}
