using ZoneTree;
using ZoneTree.Comparers;
using ZoneTree.Core;
using ZoneTree.Exceptions;
using ZoneTree.Options;
using ZoneTree.Serializers;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeTreeFactory
{
    private const string OriginalTreeMissing = "Original store inspection requires the existing execution tree directory.";

    internal static IZoneTree<Memory<byte>, Memory<byte>> Open(ZoneTreeStoreOptions options, bool requireExisting = false)
    {
        var directory = Path.Combine(options.Directory, TreeDirectoryName);
        if (requireExisting && !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(OriginalTreeMissing);
        }
        var factory = Create(directory);
        if (!requireExisting)
        {
            return factory.OpenOrCreate();
        }
        if (!ZoneTreeMetaWAL<Memory<byte>, Memory<byte>>.Exists(factory.Options))
        {
            throw new DatabaseNotFoundException();
        }
        return factory.Open();
    }

    private static ZoneTreeFactory<Memory<byte>, Memory<byte>> Create(string directory)
        => new ZoneTreeFactory<Memory<byte>, Memory<byte>>()
            .SetDataDirectory(directory).SetComparer(new KeyComparer())
            .SetKeySerializer(new ByteArraySerializer()).SetValueSerializer(new ByteArraySerializer())
            .SetIsDeletedDelegate(static (in Memory<byte> key, in Memory<byte> value) => value.Span[0] == DeletedValueMarker)
            .SetMarkValueDeletedDelegate(static (ref Memory<byte> value) => value = new byte[] { DeletedValueMarker })
            .ConfigureWriteAheadLogOptions(o =>
            {
                // commands.wal is the only redo log and is replayed from the last checkpoint on every open,
                // so the engine WAL would fsync every mutation a second time without adding recovery coverage.
                o.WriteAheadLogMode = WriteAheadLogMode.None;
                o.CompressionMethod = CompressionMethod.None;
            })
            .ConfigureDiskSegmentOptions(o => o.CompressionMethod = CompressionMethod.None);

    private sealed class KeyComparer : IRefComparer<Memory<byte>>
    {
        public int Compare(in Memory<byte> x, in Memory<byte> y) => x.Span.SequenceCompareTo(y.Span);
    }
}
