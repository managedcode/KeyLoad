using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class EpochStorageFixture : IDisposable
{
    internal const int Native5Epoch = 5;
    internal const int Native6Epoch = 6;
    internal const int CurrentEpoch = 7;
    internal const int Native5CheckpointVersion = 3;
    internal const int Native6CheckpointVersion = 4;
    internal const long ExpectedReadGeneration = 9;
    internal const string JournalName = "commands.wal";
    internal const string UpgradeStageSuffix = ".upgrade";
    private const string RootPrefix = "keyload-data-epoch-";
    private const string SourceName = "source";
    private const string DestinationName = "destination";
    private const string BackupName = "backup";
    private const string RestoredName = "restored";
    private const string SnapshotName = "snapshot.bin";
    private const int FrameHeaderBytes = 52;
    private const int FrameLengthOffset = 8;
    private const int FramePositionOffset = 12;
    private const int FrameChecksumOffset = 20;
    private const int ChecksumBytes = 32;
    private const ulong CurrentJournalMagic = 0x344C4157444C4BUL;
    private readonly string root = Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString("N"));

    internal EpochStorageFixture() => Directory.CreateDirectory(root);

    internal string Source => Path.Combine(root, SourceName);
    internal string Destination => Path.Combine(root, DestinationName);
    internal string Backup => Path.Combine(root, BackupName);
    internal string Restored => Path.Combine(root, RestoredName);
    internal string Snapshot => Path.Combine(root, SnapshotName);
    internal ZoneTreeStoreOptions SourceOptions => new(Source);
    internal ZoneTreeStoreOptions DestinationOptions(StoreIdentity identity)
        => new(Destination) { Incarnation = identity.Incarnation, SigningKey = identity.SigningKey };
    internal byte[] FirstKey { get; } = [0x00, 0xFF, 0x31];
    internal byte[] FirstValue { get; } = [0x80, 0x00, 0x7F];
    internal byte[] SecondKey { get; } = [0x01, 0x00, 0xFE];
    internal byte[] SecondValue { get; } = [0xFF, 0x02, 0x00, 0x81];

    internal async Task<StoreIdentity> CreateNativeSourceAsync(bool checkpoint, int sourceEpoch = Native5Epoch)
    {
        if (sourceEpoch is not (Native5Epoch or Native6Epoch))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceEpoch));
        }
        StoreIdentity identity;
        using (var store = new ZoneTreeStore(SourceOptions, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()))
        {
            store.Commit((transaction, position) =>
            {
                transaction.Put(FirstKey, FirstValue);
                transaction.Put(SecondKey, SecondValue);
                transaction.Put(KeyCodec.Encode("system", "last-applied"), NativeSerialization.Serialize(position));
                return position;
            });
            store.SetDispatchPaused(true);
            if (checkpoint)
            {
                store.Compact();
            }
            identity = store.Identity with { ReadGeneration = ExpectedReadGeneration };
        }

        identity = identity with { FormatVersion = sourceEpoch };
        ZoneTreeIdentityFile.Write(Path.Combine(Source, "identity.json"), identity, UnitExecutionOptions.StorageExecution().Value.IdentityBufferBytes);
        if (checkpoint)
        {
            await RewriteCheckpointToSourceAsync(Path.Combine(Source, JournalName), sourceEpoch);
        }
        return identity;
    }

    internal static Task<Dictionary<string, byte[]?>> CaptureAsync(string directory)
        => EpochStorageSnapshot.CaptureAsync(directory);

    internal static Task AssertUnchangedAsync(string directory, Dictionary<string, byte[]?> expected)
        => EpochStorageSnapshot.AssertUnchangedAsync(directory, expected);

    internal static async Task AssertIdentityPreservedAsync(StoreIdentity expected, StoreIdentity actual)
    {
        await Assert.That(actual.FormatVersion).IsEqualTo(CurrentEpoch);
        await Assert.That(actual.KeyCodecVersion).IsEqualTo(expected.KeyCodecVersion);
        await Assert.That(actual.NodeId).IsEqualTo(expected.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(actual.SigningKey.Span.SequenceEqual(expected.SigningKey.Span)).IsTrue();
        await Assert.That(actual.Durability).IsEqualTo(expected.Durability);
        await Assert.That(actual.DispatchPaused).IsEqualTo(expected.DispatchPaused);
        await Assert.That(actual.ReadGeneration).IsEqualTo(expected.ReadGeneration);
    }

    internal static async Task RewriteCheckpointToSourceAsync(string journalPath, int sourceEpoch)
    {
        var bytes = await File.ReadAllBytesAsync(journalPath);
        await File.WriteAllBytesAsync(journalPath, CreateCurrentCheckpointAsSource(bytes, sourceEpoch));
    }

    internal static byte[] CreateCurrentCheckpointAsSource(ReadOnlySpan<byte> current, int sourceEpoch)
    {
        if (sourceEpoch is not (Native5Epoch or Native6Epoch))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceEpoch));
        }
        var checkpointVersion = sourceEpoch == Native5Epoch ? Native5CheckpointVersion : Native6CheckpointVersion;
        using var input = new MemoryStream(current.ToArray(), writable: false);
        using var output = new MemoryStream(current.Length);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while (input.Position < input.Length)
        {
            var header = new byte[FrameHeaderBytes];
            input.ReadExactly(header);
            var magic = BinaryPrimitives.ReadUInt64LittleEndian(header);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(FrameLengthOffset));
            var position = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(FramePositionOffset));
            var payload = new byte[length];
            input.ReadExactly(payload);
            var oldMagic = SourceMagic(magic, sourceEpoch);
            if (magic == ZoneTreePersistenceFormat.CheckpointMagic)
            {
                var metadata = NativeSerialization.Deserialize<ZoneTreeCheckpointMetadata>(payload);
                payload = NativeSerialization.Serialize(metadata with { Version = checkpointVersion });
            }
            else if (magic == ZoneTreePersistenceFormat.CheckpointEndMagic)
            {
                var footer = NativeSerialization.Deserialize<ZoneTreeCheckpointFooter>(payload);
                payload = NativeSerialization.Serialize(footer with
                { Checksum = Convert.ToHexStringLower(digest.GetHashAndReset()) });
            }
            WriteFrame(output, oldMagic, position, payload, digest,
                magic != ZoneTreePersistenceFormat.CheckpointEndMagic);
        }
        return output.ToArray();
    }

    private static ulong SourceMagic(ulong current, int sourceEpoch)
        => current switch
        {
            CurrentJournalMagic => CurrentJournalMagic,
            0x35545043444C4BUL when sourceEpoch == Native5Epoch => 0x33545043444C4BUL,
            0x35545043444C4BUL when sourceEpoch == Native6Epoch => 0x34545043444C4BUL,
            0x35415444444C4BUL when sourceEpoch == Native5Epoch => 0x33415444444C4BUL,
            0x35415444444C4BUL when sourceEpoch == Native6Epoch => 0x34415444444C4BUL,
            ZoneTreePersistenceFormat.CheckpointEndMagic when sourceEpoch == Native5Epoch => 0x33444E45444C4BUL,
            ZoneTreePersistenceFormat.CheckpointEndMagic when sourceEpoch == Native6Epoch => 0x34444E45444C4BUL,
            _ => throw new InvalidDataException("The current fixture contains an unknown checkpoint frame.")
        };

    private static void WriteFrame(Stream output, ulong magic, long position, byte[] payload,
        IncrementalHash digest, bool includeInDigest)
    {
        var header = new byte[FrameHeaderBytes];
        BinaryPrimitives.WriteUInt64LittleEndian(header, magic);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(FrameLengthOffset), payload.Length);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(FramePositionOffset), position);
        SHA256.HashData(payload, header.AsSpan(FrameChecksumOffset, ChecksumBytes));
        output.Write(header);
        output.Write(payload);
        if (includeInDigest)
        {
            digest.AppendData(header);
            digest.AppendData(payload);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
