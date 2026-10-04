using System.Buffers.Binary;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class NativeBackupCutFixture : IDisposable
{
    private const string RootPrefix = "keyload-native-backup-cut-";
    private const string AllFiles = "*";
    private readonly string root = Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString("N"));

    internal string Source => Path.Combine(root, MetadataTestContract.SourceDirectoryName);
    internal string Backup => Path.Combine(root, MetadataTestContract.BackupDirectoryName);
    internal string Destination => Path.Combine(root, MetadataTestContract.RestoredDirectoryName);
    internal string Journal => Path.Combine(Backup, MetadataTestContract.JournalFileName);
    internal StoreIdentity Identity { get; }
    internal long Position { get; }

    internal NativeBackupCutFixture(bool checkpoint = false, bool empty = false)
    {
        using var store = new ZoneTreeStore(new(Source));
        Identity = store.Identity;
        if (empty)
        {
            Position = store.CreateBackup(Backup);
            return;
        }
        store.Commit((tx, _) =>
        {
            tx.PutRecord(MetadataBackupFixture.StoredKeyBytes, MetadataBackupFixture.ExpectedValue);
            tx.PutRecord(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
                ZoneTreePersistenceFormat.LastAppliedKey), 1L);
            return true;
        });
        if (checkpoint)
        {
            store.Compact();
        }
        store.Commit((tx, _) =>
        {
            tx.PutRecord(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
                ZoneTreePersistenceFormat.ClockKey), 1L);
            return true;
        });
        Position = store.CreateBackup(Backup);
    }

    internal async Task RewriteJournalAsync(byte[] bytes)
    {
        await File.WriteAllBytesAsync(Journal, bytes);
        await MetadataTestFiles.UpdateManifestFileAsync(Backup, MetadataTestContract.JournalFileName);
    }

    internal static int LastFrameOffset(byte[] bytes)
    {
        var offset = 0;
        var last = 0;
        while (offset < bytes.Length)
        {
            last = offset;
            offset += ZoneTreePersistenceFormat.HeaderLength + BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(offset + ZoneTreePersistenceFormat.PayloadLengthOffset));
        }
        return last;
    }

    internal async Task AssertRejectedUnchangedAsync(ErrorCode expected, bool existingEmpty = false)
    {
        if (existingEmpty)
        {
            Directory.CreateDirectory(Destination);
        }
        var before = await CaptureBackupAsync();
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => ZoneTreeStore.Restore(Backup, Destination));
        await Assert.That(failure.Code).IsEqualTo(expected);
        await Assert.That(Directory.Exists(Destination)).IsEqualTo(existingEmpty);
        if (existingEmpty)
        {
            await Assert.That(Directory.EnumerateFileSystemEntries(Destination).Any()).IsFalse();
        }
        var after = await CaptureBackupAsync();
        await Assert.That(after.Keys).IsEquivalentTo(before.Keys);
        foreach (var entry in before)
        {
            await Assert.That(after[entry.Key]).IsEquivalentTo(entry.Value, CollectionOrdering.Matching);
        }
        await Assert.That(Directory.EnumerateDirectories(root).Select(path => Path.GetFileName(path)!))
            .IsEquivalentTo(existingEmpty
                ? new[] { MetadataTestContract.SourceDirectoryName, MetadataTestContract.BackupDirectoryName,
                    MetadataTestContract.RestoredDirectoryName }
                : new[] { MetadataTestContract.SourceDirectoryName, MetadataTestContract.BackupDirectoryName });
    }

    private async Task<Dictionary<string, byte[]>> CaptureBackupAsync()
    {
        var captured = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(Backup, AllFiles, SearchOption.AllDirectories))
        {
            captured.Add(Path.GetRelativePath(Backup, path), await File.ReadAllBytesAsync(path));
        }
        return captured;
    }

    public void Dispose() => Directory.Delete(root, true);
}
