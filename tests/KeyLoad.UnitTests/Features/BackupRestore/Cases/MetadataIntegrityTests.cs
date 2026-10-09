using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class MetadataIntegrityTests
{
    [Test]
    public async Task IdentityLengthMismatchFailsBeforeDestinationCreation()
    {
        using var fixture = new MetadataBackupFixture();
        await using (var identity = new FileStream(Path.Combine(fixture.BackupDirectory,
                         MetadataTestContract.IdentityFileName),
                   FileMode.Append, FileAccess.Write, FileShare.None))
        {
            await identity.WriteAsync(new[] { MetadataTestContract.TrailingPaddingByte });
        }

        await AssertOuterVerificationFailure(fixture, MetadataTestContract.IdentityLengthRestorePath);
    }

    [Test]
    public async Task IdentityOuterChecksumMismatchFailsBeforeDestinationCreation()
    {
        using var fixture = new MetadataBackupFixture();
        var path = Path.Combine(fixture.BackupDirectory, MetadataTestContract.IdentityFileName);
        var identity = await File.ReadAllBytesAsync(path);
        identity[0] ^= 1;
        await File.WriteAllBytesAsync(path, identity);

        await AssertOuterVerificationFailure(fixture, MetadataTestContract.IdentityChecksumRestorePath);
    }

    [Test]
    public async Task IdentityInnerChecksumMismatchFailsBeforeDestinationCreation()
    {
        using var fixture = new MetadataBackupFixture();
        await MetadataTestFiles.BreakIdentityEnvelopeChecksumAsync(fixture.BackupDirectory);
        var destination = Path.Combine(fixture.BackupDirectory,
            MetadataTestContract.IdentityInnerChecksumRestorePath);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            ZoneTreeStore.Restore(fixture.BackupDirectory, destination, UnitExecutionOptions.StorageExecution()));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(MetadataTestContract.IdentityChecksumInvalidDetail);
        await Assert.That(Directory.Exists(destination)).IsFalse();
    }

    [Test]
    public async Task CorruptWalVerificationPrecedesUnsupportedIdentityParsing()
    {
        using var fixture = new MetadataBackupFixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.BackupDirectory,
            MetadataTestContract.IdentityFileName), MetadataTestContract.UnsupportedIdentityContents);
        await MetadataTestFiles.UpdateManifestFileAsync(fixture.BackupDirectory,
            MetadataTestContract.IdentityFileName);
        var journalPath = Path.Combine(fixture.BackupDirectory, MetadataTestContract.JournalFileName);
        var journal = await File.ReadAllBytesAsync(journalPath);
        journal[^1] ^= 1;
        await File.WriteAllBytesAsync(journalPath, journal);
        var destination = Path.Combine(fixture.BackupDirectory,
            MetadataTestContract.VerificationOrderRestorePath);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            ZoneTreeStore.Restore(fixture.BackupDirectory, destination, UnitExecutionOptions.StorageExecution()));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(MetadataTestContract.BackupFileVerificationFailedDetail);
        await Assert.That(Directory.Exists(destination)).IsFalse();
    }

    [Test]
    public async Task ReparsePointCanonicalFileFailsBeforeDestinationCreation()
    {
        using var fixture = new MetadataBackupFixture();
        var parent = Directory.GetParent(fixture.BackupDirectory) ?? throw new DirectoryNotFoundException();
        var destination = Path.Combine(parent.FullName, MetadataTestContract.LinkRestorePath);
        await MetadataLinkRestoreContinuation.RequireAsync(fixture, destination,
            TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
    }

    [Test]
    public async Task NonemptyDestinationConflictsBeforeOversizedManifestIsRead()
    {
        using var fixture = new MetadataBackupFixture();
        await MetadataTestFiles.PadWithTrailingBytesAsync(
            Path.Combine(fixture.BackupDirectory, MetadataTestContract.ManifestFileName),
            MetadataTestContract.ManifestLimitBytes + MetadataTestContract.OneByte);
        var destination = Path.Combine(fixture.BackupDirectory, MetadataTestContract.ExistingDestinationPath);
        Directory.CreateDirectory(destination);
        var existingPath = Path.Combine(destination, MetadataTestContract.PreservedFileName);
        await File.WriteAllTextAsync(existingPath, MetadataTestContract.PreservedFileContents);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            ZoneTreeStore.Restore(fixture.BackupDirectory, destination, UnitExecutionOptions.StorageExecution()));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(failure.Message).IsEqualTo(MetadataTestContract.RestoreDestinationNotEmptyDetail);
        await Assert.That(await File.ReadAllTextAsync(existingPath))
            .IsEqualTo(MetadataTestContract.PreservedFileContents);
    }

    [Test]
    public async Task UnsupportedManifestVersionFailsBeforeDestinationCreation()
    {
        using var fixture = new MetadataBackupFixture();
        var manifest = await MetadataTestFiles.ReadManifestAsync(fixture.BackupDirectory);
        await MetadataTestFiles.WriteManifestAsync(fixture.BackupDirectory,
            manifest with { Version = MetadataTestContract.UnsupportedManifestVersion });
        var destination = Path.Combine(fixture.BackupDirectory,
            MetadataTestContract.UnsupportedVersionRestorePath);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            ZoneTreeStore.Restore(fixture.BackupDirectory, destination, UnitExecutionOptions.StorageExecution()));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(failure.Message).IsEqualTo(MetadataTestContract.ManifestUnsupportedDetail);
        await Assert.That(Directory.Exists(destination)).IsFalse();
    }

    private static async Task AssertOuterVerificationFailure(MetadataBackupFixture fixture, string suffix)
    {
        var destination = Path.Combine(fixture.BackupDirectory, suffix);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            ZoneTreeStore.Restore(fixture.BackupDirectory, destination, UnitExecutionOptions.StorageExecution()));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(MetadataTestContract.BackupFileVerificationFailedDetail);
        await Assert.That(Directory.Exists(destination)).IsFalse();
    }
}
