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
    // Point operations must not decompress the native 4 MiB default block to read one small record.
    private const int TransactionalCompressionBlockBytes = 1024 * 1024;

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
        ZoneTreeWriteAheadLogAdmission.RequireStoredProcessPersistence(factory);
        var tree = requireExisting ? factory.OpenTransactional() : factory.OpenOrCreateTransactional();
        // Native commit sync also retires MVCC commit state; the engine owns its timer and joined shutdown.
        tree.AutoSyncInterval = TimeSpan.FromMilliseconds(new WriteAheadLogOptions()
            .SyncCompressedModeOptions.TailWriterJobInterval);
        return tree;
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
            })
            .ConfigureWriteAheadLogOptions(ConfigureWriteAheadLog)
            .ConfigureDiskSegmentOptions(ConfigureTransactionalDiskSegment);

    private static ZoneTreeFactory<Memory<byte>, Memory<byte>> Create(string directory)
        => new ZoneTreeFactory<Memory<byte>, Memory<byte>>()
            .SetDataDirectory(directory).SetComparer(new KeyComparer())
            .SetKeySerializer(new ByteArraySerializer()).SetValueSerializer(new ByteArraySerializer())
            .Configure(o => o.BTreeLeafSize = MutableLeafSize)
            .ConfigureWriteAheadLogOptions(options =>
            {
                ConfigureWriteAheadLog(options);
                options.WriteAheadLogMode = WriteAheadLogMode.Shared;
                // The transactional tree's native AutoSyncInterval syncs its complete WAL and commit state.
                options.SyncCompressedModeOptions.EnableTailWriterJob = false;
            })
            .ConfigureDiskSegmentOptions(ConfigureTransactionalDiskSegment);

    private static void ConfigureTransactionalDiskSegment(DiskSegmentOptions options)
    {
        options.CompressionMethod = CompressionMethod.LZ4;
        options.CompressionLevel = CompressionLevels.LZ4Fastest;
        options.CompressionBlockSize = TransactionalCompressionBlockBytes;
    }

    // SyncCompressed synchronously publishes records into the native mapped WAL tail before returning.
    // This is the ProcessDurable acknowledgement boundary; disk sync remains engine-owned background work.
    private static void ConfigureWriteAheadLog(WriteAheadLogOptions options)
    {
        options.WriteAheadLogMode = WriteAheadLogMode.SyncCompressed;
        options.CompressionMethod = CompressionMethod.None;
    }

    private sealed class KeyComparer : IRefComparer<Memory<byte>>
    {
        public int Compare(in Memory<byte> x, in Memory<byte> y) => x.Span.SequenceCompareTo(y.Span);
    }
}
