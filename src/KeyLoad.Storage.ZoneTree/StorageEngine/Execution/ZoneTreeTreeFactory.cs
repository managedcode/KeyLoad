using ZoneTree;
using ZoneTree.Comparers;
using ZoneTree.Options;
using ZoneTree.Serializers;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeTreeFactory
{
    private const string OriginalTreeMissing = "Original store inspection requires the existing execution tree directory.";

    internal static ITransactionalZoneTree<Memory<byte>, Memory<byte>> Open(ZoneTreeStoreOptions options, bool requireExisting = false)
    {
        var directory = Path.Combine(options.Directory, TreeDirectoryName);
        if (requireExisting && !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(OriginalTreeMissing);
        }
        var factory = Create(directory);
        if (!requireExisting)
        {
            return factory.OpenOrCreateTransactional();
        }
        return factory.OpenTransactional();
    }

    private static ZoneTreeFactory<Memory<byte>, Memory<byte>> Create(string directory)
        => new ZoneTreeFactory<Memory<byte>, Memory<byte>>()
            .SetDataDirectory(directory).SetComparer(new KeyComparer())
            .SetKeySerializer(new ByteArraySerializer()).SetValueSerializer(new ByteArraySerializer())
            .ConfigureWriteAheadLogOptions(o =>
            {
                // The durable KeyLoad command journal supplies ordered replay and acknowledgements.
                // MVCC owns atomic visibility; its versions are rebuilt by that replay after a crash.
                o.WriteAheadLogMode = WriteAheadLogMode.None;
                o.CompressionMethod = CompressionMethod.None;
            })
            .ConfigureDiskSegmentOptions(o => o.CompressionMethod = CompressionMethod.None);

    private sealed class KeyComparer : IRefComparer<Memory<byte>>
    {
        public int Compare(in Memory<byte> x, in Memory<byte> y) => x.Span.SequenceCompareTo(y.Span);
    }
}
