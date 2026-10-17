using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class MissingBackupManifestTests
{
    private const string MissingBackupDirectoryName = "missing-backup";
    private const string EmptyBackupDirectoryName = "empty-backup";

    [Test]
    public Task AcBackup002MissingBackupDirectoryRejectsBeforePublication()
        => AssertRejectedWithoutMutationAsync(MissingBackupDirectoryName, createDirectory: false);

    [Test]
    public Task AcBackup002EmptyBackupDirectoryRejectsBeforePublication()
        => AssertRejectedWithoutMutationAsync(EmptyBackupDirectoryName, createDirectory: true);

    private static async Task AssertRejectedWithoutMutationAsync(string directoryName, bool createDirectory)
    {
        using var fixture = new MetadataBackupFixture();
        var token = TestContext.Current!.Execution.CancellationToken;
        var policy = UnitExecutionOptions.StorageExecution();
        var root = Path.GetDirectoryName(fixture.SourceDirectory)!;
        var backup = Path.Combine(root, directoryName);
        if (createDirectory)
        {
            Directory.CreateDirectory(backup);
        }
        var entries = Directory.EnumerateFileSystemEntries(root).Order(StringComparer.Ordinal).ToArray();
        var sourceFiles = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.SourceDirectory, token);
        var backupFiles = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.BackupDirectory, token);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            ZoneTreeStore.Restore(backup, fixture.RestoredDirectory, policy));

        await MetadataRestoreAssertions.AssertUnsupported(failure, MetadataTestContract.ManifestUnsupportedDetail);
        await Assert.That(Directory.Exists(fixture.RestoredDirectory)).IsFalse();
        await Assert.That(Directory.Exists(backup)).IsEqualTo(createDirectory);
        if (createDirectory)
        {
            await Assert.That(Directory.EnumerateFileSystemEntries(backup).ToArray()).IsEmpty();
        }
        await Assert.That(Directory.EnumerateFileSystemEntries(root).Order(StringComparer.Ordinal)
            .SequenceEqual(entries, StringComparer.Ordinal)).IsTrue();
        await CliBackupRestoreAssertions.FilesEqualAsync(sourceFiles, fixture.SourceDirectory, token);
        await CliBackupRestoreAssertions.FilesEqualAsync(backupFiles, fixture.BackupDirectory, token);
        await AssertOriginalStoreReopensAsync(fixture);
        await MetadataRestoreAssertions.AssertRestored(fixture, fixture.RestoredDirectory);
    }

    private static async Task AssertOriginalStoreReopensAsync(MetadataBackupFixture fixture)
    {
        using var reopened = new ZoneTreeStore(new(fixture.SourceDirectory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        await Assert.That(reopened.Identity.NodeId).IsEqualTo(fixture.OriginalIdentity.NodeId);
        await Assert.That(reopened.Identity.Incarnation).IsEqualTo(fixture.OriginalIdentity.Incarnation);
        await Assert.That(reopened.Identity.DispatchPaused).IsEqualTo(fixture.OriginalIdentity.DispatchPaused);
        await Assert.That(reopened.Identity.SigningKey.Span.SequenceEqual(fixture.OriginalIdentity.SigningKey.Span)).IsTrue();
        await Assert.That(reopened.Read(view => NativeSerialization.Deserialize<string>(
                view.ReadOwnedValue(MetadataBackupFixture.StoredKeyBytes)!)))
            .IsEqualTo(MetadataBackupFixture.ExpectedValue);
    }
}
