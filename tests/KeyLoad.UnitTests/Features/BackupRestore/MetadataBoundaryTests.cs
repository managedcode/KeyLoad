using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class MetadataBoundaryTests
{
    [Test]
    public async Task ManifestWhitespaceAtInclusiveLimitRestoresAndOneOverFailsBeforeDestination()
    {
        using var fixture = new MetadataBackupFixture();
        var manifestPath = Path.Combine(fixture.BackupDirectory, MetadataTestContract.ManifestFileName);
        await MetadataTestFiles.PadWithWhitespaceAsync(manifestPath, MetadataTestContract.ManifestLimitBytes);

        await MetadataRestoreAssertions.AssertRestored(fixture, fixture.RestoredDirectory);

        await MetadataTestFiles.PadWithWhitespaceAsync(manifestPath,
            MetadataTestContract.ManifestLimitBytes + MetadataTestContract.OneByte);
        var destination = Path.Combine(fixture.BackupDirectory, MetadataTestContract.ManifestOverLimitPath);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => ZoneTreeStore.Restore(fixture.BackupDirectory, destination));
        await MetadataRestoreAssertions.AssertUnsupported(failure, MetadataTestContract.ManifestUnsupportedDetail);
        await Assert.That(Directory.Exists(destination)).IsFalse();
    }

    [Test]
    public async Task IdentityWhitespaceAtInclusiveLimitRestoresAndOneOverFailsBeforeDestination()
    {
        using var fixture = new MetadataBackupFixture();
        var identityPath = Path.Combine(fixture.BackupDirectory, MetadataTestContract.IdentityFileName);
        await MetadataTestFiles.PadWithWhitespaceAsync(identityPath, MetadataTestContract.IdentityLimitBytes);
        await MetadataTestFiles.UpdateManifestFileAsync(fixture.BackupDirectory, MetadataTestContract.IdentityFileName);

        await MetadataRestoreAssertions.AssertRestored(fixture, fixture.RestoredDirectory);

        await MetadataTestFiles.PadWithWhitespaceAsync(identityPath,
            MetadataTestContract.IdentityLimitBytes + MetadataTestContract.OneByte);
        await MetadataTestFiles.UpdateManifestFileAsync(fixture.BackupDirectory, MetadataTestContract.IdentityFileName);
        var destination = Path.Combine(fixture.BackupDirectory, MetadataTestContract.IdentityOverLimitPath);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => ZoneTreeStore.Restore(fixture.BackupDirectory, destination));
        await MetadataRestoreAssertions.AssertUnsupported(failure, MetadataTestContract.IdentityFormatUnsupportedDetail);
        await Assert.That(Directory.Exists(destination)).IsFalse();
    }

    [Test]
    public async Task StartupIdentityBoundaryFailureReleasesLockAndOriginalBytesReopen()
    {
        using var fixture = new MetadataBackupFixture();
        var identityPath = Path.Combine(fixture.SourceDirectory, MetadataTestContract.IdentityFileName);
        var originalBytes = await File.ReadAllBytesAsync(identityPath);
        await MetadataTestFiles.PadWithWhitespaceAsync(identityPath, MetadataTestContract.IdentityLimitBytes);
        using (var exactLimit = new ZoneTreeStore(new(fixture.SourceDirectory)))
        {
            await Assert.That(exactLimit.Identity.Incarnation).IsEqualTo(fixture.OriginalIdentity.Incarnation);
        }

        await MetadataTestFiles.PadWithWhitespaceAsync(identityPath,
            MetadataTestContract.IdentityLimitBytes + MetadataTestContract.OneByte);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var overLimit = new ZoneTreeStore(new(fixture.SourceDirectory));
        });
        await MetadataRestoreAssertions.AssertUnsupported(failure, MetadataTestContract.IdentityFormatUnsupportedDetail);
        await using (var releasedOwnership = new FileStream(Path.Combine(fixture.SourceDirectory,
                         MetadataTestContract.OwnerLockFileName),
                   FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
        }

        await File.WriteAllBytesAsync(identityPath, originalBytes);
        using var reopened = new ZoneTreeStore(new(fixture.SourceDirectory));
        await Assert.That(reopened.Identity.Incarnation).IsEqualTo(fixture.OriginalIdentity.Incarnation);
        await Assert.That(reopened.Read(view => JsonDefaults.Deserialize<string>(
                view.ReadOwnedValue(MetadataBackupFixture.StoredKeyBytes)!)))
            .IsEqualTo(MetadataBackupFixture.ExpectedValue);
    }
}
