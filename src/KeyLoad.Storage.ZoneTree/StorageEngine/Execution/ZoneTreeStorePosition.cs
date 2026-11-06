using ZoneTree;
using ZoneTree.Transactional;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Reads the engine's published MVCC cut without creating a shared write dependency.</summary>
internal static class ZoneTreeStorePosition
{
    internal static long Read(ITransactionalZoneTree<Memory<byte>, Memory<byte>> tree)
    {
        using var read = tree.BeginReadOnlyTransaction();
        return Read(read);
    }

    internal static long Read(IReadTransaction<Memory<byte>, Memory<byte>> read)
        => read.SnapshotTimestamp;
}
