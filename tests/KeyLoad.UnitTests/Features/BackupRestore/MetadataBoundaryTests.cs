using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class MetadataBoundaryTests
{
    [Test]
    public async Task ManifestTrailingBytesAtInclusiveLimitRejectAndOneOverFailsBeforeDestination()
    {
        using var fixture = new MetadataBackupFixture();
        var manifestPath = Path.Combine(fixture.BackupDirectory, MetadataTestContract.ManifestFileName);
        var original = await File.ReadAllBytesAsync(manifestPath);
        await MetadataTestFiles.PadWithTrailingBytesAsync(manifestPath, MetadataTestContract.ManifestLimitBytes);
        var atLimit = Path.Combine(fixture.BackupDirectory, MetadataTestContract.ManifestAtLimitPath);
        await AssertCorruptRestore(fixture, atLimit);

        await MetadataTestFiles.PadWithTrailingBytesAsync(manifestPath,
            MetadataTestContract.ManifestLimitBytes + MetadataTestContract.OneByte);
        var destination = Path.Combine(fixture.BackupDirectory, MetadataTestContract.ManifestOverLimitPath);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => ZoneTreeStore.Restore(fixture.BackupDirectory, destination));
        await MetadataRestoreAssertions.AssertUnsupported(failure, MetadataTestContract.ManifestUnsupportedDetail);
        await Assert.That(Directory.Exists(destination)).IsFalse();
        await File.WriteAllBytesAsync(manifestPath, original);
        await MetadataRestoreAssertions.AssertRestored(fixture, fixture.RestoredDirectory);
    }

    [Test]
    public async Task IdentityTrailingBytesAtInclusiveLimitRejectAndOneOverFailsBeforeDestination()
    {
        using var fixture = new MetadataBackupFixture();
        var identityPath = Path.Combine(fixture.BackupDirectory, MetadataTestContract.IdentityFileName);
        var original = await File.ReadAllBytesAsync(identityPath);
        await MetadataTestFiles.PadWithTrailingBytesAsync(identityPath, MetadataTestContract.IdentityLimitBytes);
        await MetadataTestFiles.UpdateManifestFileAsync(fixture.BackupDirectory, MetadataTestContract.IdentityFileName);
        var atLimit = Path.Combine(fixture.BackupDirectory, MetadataTestContract.IdentityAtLimitPath);
        await AssertCorruptRestore(fixture, atLimit);

        await MetadataTestFiles.PadWithTrailingBytesAsync(identityPath,
            MetadataTestContract.IdentityLimitBytes + MetadataTestContract.OneByte);
        await MetadataTestFiles.UpdateManifestFileAsync(fixture.BackupDirectory, MetadataTestContract.IdentityFileName);
        var destination = Path.Combine(fixture.BackupDirectory, MetadataTestContract.IdentityOverLimitPath);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => ZoneTreeStore.Restore(fixture.BackupDirectory, destination));
        await MetadataRestoreAssertions.AssertUnsupported(failure, MetadataTestContract.IdentityFormatUnsupportedDetail);
        await Assert.That(Directory.Exists(destination)).IsFalse();
        await File.WriteAllBytesAsync(identityPath, original);
        await MetadataTestFiles.UpdateManifestFileAsync(fixture.BackupDirectory, MetadataTestContract.IdentityFileName);
        await MetadataRestoreAssertions.AssertRestored(fixture, fixture.RestoredDirectory);
    }

    [Test]
    public async Task StartupIdentityBoundaryFailuresReleaseLockAndOriginalBytesReopen()
    {
        using var fixture = new MetadataBackupFixture();
        var identityPath = Path.Combine(fixture.SourceDirectory, MetadataTestContract.IdentityFileName);
        var originalBytes = await File.ReadAllBytesAsync(identityPath);
        await MetadataTestFiles.PadWithTrailingBytesAsync(identityPath, MetadataTestContract.IdentityLimitBytes);
        await AssertStartupFailureReleasesLock(fixture, ErrorCode.Corruption);
        await MetadataTestFiles.PadWithTrailingBytesAsync(identityPath,
            MetadataTestContract.IdentityLimitBytes + MetadataTestContract.OneByte);
        await AssertStartupFailureReleasesLock(fixture, ErrorCode.FormatUnsupported);
        await File.WriteAllBytesAsync(identityPath, originalBytes);
        using var reopened = new ZoneTreeStore(new(fixture.SourceDirectory));
        await Assert.That(reopened.Identity.Incarnation).IsEqualTo(fixture.OriginalIdentity.Incarnation);
        await Assert.That(reopened.Read(view => NativeSerialization.Deserialize<string>(
                view.ReadOwnedValue(MetadataBackupFixture.StoredKeyBytes)!)))
            .IsEqualTo(MetadataBackupFixture.ExpectedValue);
    }

    private static async Task AssertCorruptRestore(MetadataBackupFixture fixture, string destination)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => ZoneTreeStore.Restore(fixture.BackupDirectory, destination));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(Directory.Exists(destination)).IsFalse();
    }

    private static async Task AssertStartupFailureReleasesLock(MetadataBackupFixture fixture, ErrorCode expected)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var invalid = new ZoneTreeStore(new(fixture.SourceDirectory));
        });
        await Assert.That(failure.Code).IsEqualTo(expected);
        if (expected == ErrorCode.FormatUnsupported)
        {
            await MetadataRestoreAssertions.AssertUnsupported(failure, MetadataTestContract.IdentityFormatUnsupportedDetail);
        }
        await using var releasedOwnership = new FileStream(Path.Combine(fixture.SourceDirectory,
            MetadataTestContract.OwnerLockFileName), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }
}
