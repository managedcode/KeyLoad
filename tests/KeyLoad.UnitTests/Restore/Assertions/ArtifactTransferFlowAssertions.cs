using KeyLoad.Artifacts;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class ArtifactTransferFlowAssertions
{
    private const int ValidPieceBytes = 1_024;
    private const int CanonicalFileCount = 3;
    private const string JournalFileName = "commands.wal";

    internal static async Task AssertUnpublishedAndPreservedAsync(CliBackupRestoreFixture fixture,
        IReadOnlyDictionary<string, byte[]> source, IReadOnlyDictionary<string, byte[]> backup,
        CancellationToken cancellationToken)
    {
        await Assert.That(File.Exists(fixture.ArtifactPath)).IsFalse();
        await Assert.That(Directory.Exists(fixture.CopiedArtifactDirectory)).IsFalse();
        await Assert.That(Directory.Exists(fixture.UnpackedDirectory)).IsFalse();
        await Assert.That(Directory.Exists(fixture.RestoredDirectory)).IsFalse();
        await CliBackupRestoreAssertions.FilesEqualAsync(source, fixture.SourceDirectory, cancellationToken);
        await CliBackupRestoreAssertions.FilesEqualAsync(backup, fixture.BackupDirectory, cancellationToken);
    }

    internal static async Task RunHealthyArchiveRestoreAsync(CliBackupRestoreFixture fixture,
        IReadOnlyDictionary<string, byte[]> source, IReadOnlyDictionary<string, byte[]> backup,
        CancellationToken cancellationToken)
    {
        BackupArtifact.Pack(fixture.BackupDirectory, fixture.ArtifactPath, ValidPieceBytes);
        var entries = BackupArtifact.Inspect(fixture.ArtifactPath);
        await Assert.That(entries.Length).IsEqualTo(CanonicalFileCount);
        await Assert.That(entries.Single(entry => entry.Name == JournalFileName).Pieces > 1).IsTrue();
        await FileStorageCopyAsync(fixture, cancellationToken);
        BackupArtifact.Unpack(fixture.CopiedArtifactPath, fixture.UnpackedDirectory);
        await CliBackupRestoreAssertions.FilesEqualAsync(backup, fixture.UnpackedDirectory, cancellationToken);
        await RestoreAndVerifyAsync(fixture);
        await CliBackupRestoreAssertions.FilesEqualAsync(source, fixture.SourceDirectory, cancellationToken);
        await CliBackupRestoreAssertions.FilesEqualAsync(backup, fixture.BackupDirectory, cancellationToken);
    }

    private static async Task FileStorageCopyAsync(CliBackupRestoreFixture fixture,
        CancellationToken cancellationToken)
    {
        var copied = await ArtifactTransfer.CopyToFileStorageAsync(fixture.ArtifactPath,
            fixture.CopiedArtifactDirectory, cancellationToken);
        await Assert.That(copied.IsSuccess).IsTrue();
        var originalBytes = await File.ReadAllBytesAsync(fixture.ArtifactPath, cancellationToken);
        var copiedBytes = await File.ReadAllBytesAsync(fixture.CopiedArtifactPath, cancellationToken);
        await Assert.That(copiedBytes).IsEquivalentTo(originalBytes, CollectionOrdering.Matching);
    }

    private static async Task RestoreAndVerifyAsync(CliBackupRestoreFixture fixture)
    {
        var identity = ZoneTreeStore.Restore(fixture.UnpackedDirectory, fixture.RestoredDirectory,
            UnitExecutionOptions.StorageExecution());
        await Assert.That(identity.Incarnation).IsNotEqualTo(fixture.OriginalIdentity.Incarnation);
        await Assert.That(identity.DispatchPaused).IsTrue();
        using var reopened = new ZoneTreeStore(new(fixture.RestoredDirectory),
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var restored = reopened.Read(view => NativeSerialization.Deserialize<string>(
            view.ReadOwnedValue(ArtifactTransferFlowFixture.LargePayloadKeyBytes)!));
        await Assert.That(restored).IsEqualTo(ArtifactTransferFlowFixture.LargePayload);
        var seeded = reopened.Read(view => NativeSerialization.Deserialize<string>(
            view.ReadOwnedValue(CliBackupRestoreFixture.StoredKeyBytes)!));
        await Assert.That(seeded).IsEqualTo(CliBackupRestoreFixture.StoredValue);
        await Assert.That(reopened.Identity.Incarnation).IsEqualTo(identity.Incarnation);
        await Assert.That(reopened.Identity.DispatchPaused).IsTrue();
    }
}
