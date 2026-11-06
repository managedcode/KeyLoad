using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.ProcessRecoveryTests;

internal static class NativeStoreAssertions
{
    internal static long Applied(IAtomicStore store, PartitionRef? partition)
        => store.Read(view => AppliedInView(view, partition));

    internal static long AppliedInView(IKeyValueView view, PartitionRef? partition)
    {
        var bytes = view.ReadOwnedValue(StorageCommandKeys.Applied(partition));
        if (bytes is null)
        {
            return 0L;
        }
        var actual = OrleansSerialization.Deserialize<StoragePartitionPosition>(bytes);
        if (actual.Partition != partition || actual.Position < 0)
        {
            throw new InvalidOperationException("The original stored command prefix does not match its expected partition.");
        }
        return actual.Position;
    }

    internal static async Task<long> ReopenedAsync(IAtomicStore store, long previous)
    {
        var current = store.Position;
        await Assert.That(current).IsGreaterThanOrEqualTo(previous);
        return current;
    }

    internal static async Task<long> AdvancedAsync(IAtomicStore store, long previous)
    {
        var current = store.Position;
        await Assert.That(current).IsGreaterThan(previous);
        return current;
    }
}
