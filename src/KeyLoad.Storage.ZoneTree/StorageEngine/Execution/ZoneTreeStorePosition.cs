using System.Buffers.Binary;
using System.Runtime.InteropServices;
using ZoneTree;
using ZoneTree.Transactional;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Persists the store's local commit position inside the same ZoneTree transaction as each commit.</summary>
internal static class ZoneTreeStorePosition
{
    private const long InitialPosition = 0;
    private static readonly byte[] Key = KeyCodec.Encode(SystemNamespace, PositionKey);

    internal static void Stage(ITransaction<Memory<byte>, Memory<byte>> transaction, long position)
    {
        var value = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(value, position);
        transaction.Upsert(MemoryMarshal.AsMemory<byte>(Key), value);
    }

    internal static long Read(ITransactionalZoneTree<Memory<byte>, Memory<byte>> tree)
    {
        using var read = tree.BeginReadOnlyTransaction();
        return Read(read);
    }

    internal static long Read(IReadTransaction<Memory<byte>, Memory<byte>> read)
        => read.TryGet(Key, out var value) ? BinaryPrimitives.ReadInt64LittleEndian(value.Span) : InitialPosition;
}
