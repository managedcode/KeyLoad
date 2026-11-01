using ZoneTree;
using ZoneTree.Comparers;
using ZoneTree.Options;
using ZoneTree.Serializers;
using ZoneTree.Transactional.Versioning;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;
using PlainValue = ZoneTree.PresetTypes.Deletable<System.Memory<byte>>;
using StorageKey = ZoneTree.Transactional.Versioning.VersionedKey<System.Memory<byte>>;
using StorageValue = ZoneTree.Transactional.Versioning.VersionedValue<System.Memory<byte>>;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeTreeFactory
{
    private const string OriginalTreeMissing = "Original store inspection requires the existing execution tree directory.";
    // MVCC point reads snapshot a mutable leaf; keep those copied key/value arrays small.
    private const int MutableLeafSize = 32;

    internal static ZoneTreeFactory<Memory<byte>, PlainValue> Plain(string directory)
        => new ZoneTreeFactory<Memory<byte>, PlainValue>()
            .SetDataDirectory(directory).SetComparer(new KeyComparer())
            .SetKeySerializer(new ByteArraySerializer())
            .SetValueSerializer(new DeletableRefSerializer<Memory<byte>>(new ByteArraySerializer()))
            .SetIsDeletedDelegate(static (in Memory<byte> _, in PlainValue value) => value.IsDeleted)
            .SetMarkValueDeletedDelegate(static (ref PlainValue value) => value = new(value.Value, true))
            .ConfigureWriteAheadLogOptions(ConfigureWriteAheadLog)
            .ConfigureDiskSegmentOptions(o => o.CompressionMethod = CompressionMethod.None);

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

    /// <summary>Configures the plain tree that stores the MVCC tree's versions exactly as ZoneTree's transactional
    /// tree configures it, so ZoneTree can restore a live backup of that storage into a store directory.</summary>
    internal static ZoneTreeFactory<StorageKey, StorageValue> Storage(string directory)
        => new ZoneTreeFactory<StorageKey, StorageValue>()
            .SetDataDirectory(directory)
            .SetComparer(new VersionedKeyComparer<Memory<byte>>(new KeyComparer()))
            .SetKeySerializer(new VersionedKeySerializer<Memory<byte>>(new ByteArraySerializer()))
            .SetValueSerializer(new VersionedValueSerializer<Memory<byte>>(new ByteArraySerializer()))
            .SetIsDeletedDelegate(static (in StorageKey _, in StorageValue value) => value.Kind == VersionedValueKind.Deleted)
            .SetMarkValueDeletedDelegate(static (ref StorageValue value) => value = StorageValue.Deleted)
            .Configure(o =>
            {
                o.BTreeLeafSize = MutableLeafSize;
                o.MutableSegmentBloomFilterBitsPerItem = 0;
                o.PreserveWriteOrderAcrossSegments = true;
            })
            .ConfigureWriteAheadLogOptions(ConfigureWriteAheadLog)
            .ConfigureDiskSegmentOptions(o => o.CompressionMethod = CompressionMethod.None);

    private static ZoneTreeFactory<Memory<byte>, Memory<byte>> Create(string directory)
        => new ZoneTreeFactory<Memory<byte>, Memory<byte>>()
            .SetDataDirectory(directory).SetComparer(new KeyComparer())
            .SetKeySerializer(new ByteArraySerializer()).SetValueSerializer(new ByteArraySerializer())
            .Configure(o => o.BTreeLeafSize = MutableLeafSize)
            .ConfigureWriteAheadLogOptions(ConfigureWriteAheadLog)
            .ConfigureDiskSegmentOptions(o => o.CompressionMethod = CompressionMethod.None);

    // ZoneTree's write-ahead log is the only redo log: MVCC recovery drops a transaction whose commit never reached
    // it. Its writer thread batches concurrent SyncWALAsync calls into one fsync without blocking the callers, which
    // acknowledge only after that sync.
    private static void ConfigureWriteAheadLog(WriteAheadLogOptions options)
    {
        options.WriteAheadLogMode = WriteAheadLogMode.AsyncCompressed;
        options.CompressionMethod = CompressionMethod.None;
    }

    private sealed class KeyComparer : IRefComparer<Memory<byte>>
    {
        public int Compare(in Memory<byte> x, in Memory<byte> y) => x.Span.SequenceCompareTo(y.Span);
    }
}
