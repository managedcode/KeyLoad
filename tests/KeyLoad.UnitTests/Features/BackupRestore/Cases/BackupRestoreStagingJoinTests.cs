using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

/// <summary>AC-SQLC-011/AC-BACKUP-002: verified staging publishes without changing the original backup.</summary>
internal sealed class BackupRestoreStagingJoinTests
{
    private const string AllEntriesPattern = "*";
    private const string MissingParent = "The private backup fixture has no parent directory.";
    private const long RestoreAuthorityCommits = 1;
    private const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    [Test, Arguments(false), Arguments(true)]
    public async Task AcSqlc011AbsentTargetPublishesNativeCutAndPreservesEveryBackupByte(bool trailingSeparator)
    {
        using var fixture = new NativeBackupCutFixture();
        await AssertStagedRestoreAsync(fixture, existingEmpty: false, trailingSeparator);
    }

    [Test, Arguments(false), Arguments(true)]
    public async Task AcSqlc011ExistingEmptyTargetPublishesNativeCheckpointAndPreservesEveryBackupByte(bool trailingSeparator)
    {
        using var fixture = new NativeBackupCutFixture(checkpoint: true);
        await AssertStagedRestoreAsync(fixture, existingEmpty: true, trailingSeparator);
    }

    private static async Task AssertStagedRestoreAsync(NativeBackupCutFixture fixture, bool existingEmpty, bool trailingSeparator)
    {
        if (existingEmpty)
        {
            Directory.CreateDirectory(fixture.Destination);
            await Assert.That(Directory.EnumerateFileSystemEntries(fixture.Destination).Any()).IsFalse();
        }
        await Assert.That(Directory.Exists(fixture.Destination)).IsEqualTo(existingEmpty);
        var original = await CaptureBackupAsync(fixture.Backup);
        foreach (var name in new[] { ZoneTreePersistenceFormat.IdentityFileName,
                     ZoneTreePersistenceFormat.JournalFileName, ZoneTreePersistenceFormat.BackupManifestFileName })
        {
            await Assert.That(original.ContainsKey(name)).IsTrue();
        }
        var requestedDestination = trailingSeparator
            ? fixture.Destination + Path.DirectorySeparatorChar : fixture.Destination;
        var identity = ZoneTreeStore.Restore(fixture.Backup, requestedDestination);
        await AssertRestoredDataAsync(fixture, identity);
        await AssertPublishedDirectoryAsync(fixture);
        await AssertBackupUnchangedAsync(fixture.Backup, original);
    }

    private static async Task AssertRestoredDataAsync(NativeBackupCutFixture fixture, StoreIdentity identity)
    {
        await Assert.That(identity.NodeId).IsNotEqualTo(fixture.Identity.NodeId);
        await Assert.That(identity.Incarnation).IsNotEqualTo(fixture.Identity.Incarnation);
        await Assert.That(identity.SigningKey.Span.SequenceEqual(fixture.Identity.SigningKey.Span)).IsFalse();
        await Assert.That(identity.DispatchPaused).IsTrue();
        using var restored = new ZoneTreeStore(new(fixture.Destination));
        await Assert.That(restored.Position).IsEqualTo(fixture.Position + RestoreAuthorityCommits);
        await Assert.That(restored.Read(view => NativeSerialization.Deserialize<string>(
                view.ReadOwnedValue(MetadataBackupFixture.StoredKeyBytes)!)))
            .IsEqualTo(MetadataBackupFixture.ExpectedValue);
        await Assert.That(restored.Read(view => NativeSerialization.Deserialize<bool>(view.ReadOwnedValue(
            KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace, ZoneTreePersistenceFormat.DispatchPausedKey))!)))
            .IsTrue();
        await Assert.That(restored.Identity.NodeId).IsEqualTo(identity.NodeId);
        await Assert.That(restored.Identity.Incarnation).IsEqualTo(identity.Incarnation);
        await Assert.That(restored.Identity.SigningKey.Span.SequenceEqual(identity.SigningKey.Span)).IsTrue();
        await Assert.That(restored.Identity.DispatchPaused).IsTrue();
    }

    private static async Task AssertPublishedDirectoryAsync(NativeBackupCutFixture fixture)
    {
        await Assert.That(Directory.Exists(fixture.Destination)).IsTrue();
        var parent = Directory.GetParent(fixture.Destination) ?? throw new InvalidOperationException(MissingParent);
        await Assert.That(Directory.EnumerateDirectories(parent.FullName)
                .Select(path => new DirectoryInfo(path).Name))
            .IsEquivalentTo(new[] { MetadataTestContract.SourceDirectoryName,
                MetadataTestContract.BackupDirectoryName, MetadataTestContract.RestoredDirectoryName });
        if (!OperatingSystem.IsWindows())
        {
            await Assert.That(File.GetUnixFileMode(fixture.Destination)).IsEqualTo(PrivateDirectoryMode);
        }
    }

    private static async Task<Dictionary<string, byte[]>> CaptureBackupAsync(string backup)
    {
        var captured = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(backup, AllEntriesPattern, SearchOption.AllDirectories))
        {
            captured.Add(Path.GetRelativePath(backup, path), await File.ReadAllBytesAsync(path));
        }
        return captured;
    }

    private static async Task AssertBackupUnchangedAsync(string backup, Dictionary<string, byte[]> original)
    {
        var after = await CaptureBackupAsync(backup);
        await Assert.That(after.Keys).IsEquivalentTo(original.Keys);
        foreach (var entry in original)
        {
            await Assert.That(after[entry.Key]).IsEquivalentTo(entry.Value, CollectionOrdering.Matching);
        }
    }
}
