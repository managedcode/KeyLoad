using ZoneTree;
using ZoneTree.Comparers;
using ZoneTree.Options;
using ZoneTree.Serializers;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeTreeFactory
{
    internal static IZoneTree<Memory<byte>, Memory<byte>> Open(ZoneTreeStoreOptions options)
        => new ZoneTreeFactory<Memory<byte>, Memory<byte>>()
            .SetDataDirectory(Path.Combine(options.Directory, TreeDirectoryName)).SetComparer(new KeyComparer())
            .SetKeySerializer(new ByteArraySerializer()).SetValueSerializer(new ByteArraySerializer())
            .SetIsDeletedDelegate(static (in Memory<byte> key, in Memory<byte> value) => value.Span[0] == DeletedValueMarker)
            .SetMarkValueDeletedDelegate(static (ref Memory<byte> value) => value = new byte[] { DeletedValueMarker })
            .ConfigureWriteAheadLogOptions(o =>
            {
                o.WriteAheadLogMode = WriteAheadLogMode.Sync;
                o.CompressionMethod = CompressionMethod.None;
            })
            .ConfigureDiskSegmentOptions(o => o.CompressionMethod = CompressionMethod.None).OpenOrCreate();

    private sealed class KeyComparer : IRefComparer<Memory<byte>>
    {
        public int Compare(in Memory<byte> x, in Memory<byte> y) => x.Span.SequenceCompareTo(y.Span);
    }
}
