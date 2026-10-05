using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class MetadataFormatFailureTests
{
    [Test]
    public async Task LegacyNullJsonManifestFailsAsUnsupportedBeforeDestinationCreation()
    {
        using var fixture = new MetadataBackupFixture();
        await AssertNullManifestFailure(fixture, MetadataTestContract.NullManifestJson);
    }

    [Test]
    public async Task MalformedManifestFailsBeforeDestinationCreation()
    {
        using var fixture = new MetadataBackupFixture();
        await AssertMalformedManifestFailure(fixture, MetadataTestContract.MalformedJson);
    }

    [Test]
    public async Task UnknownManifestMemberFailsBeforeDestinationCreation()
    {
        using var fixture = new MetadataBackupFixture();
        await AssertMalformedManifestFailure(fixture, MetadataTestContract.LegacyUnknownObjectJson);
    }

    [Test]
    public async Task NestedArrayManifestRootIsRejectedBeforeDestinationCreation()
    {
        using var fixture = new MetadataBackupFixture();
        var nested = new string('[', MetadataTestContract.NestedDepth) + MetadataTestContract.NestedJsonScalar
            + new string(']', MetadataTestContract.NestedDepth);
        await AssertMalformedManifestFailure(fixture, nested);
    }

    [Test]
    public async Task MissingCanonicalFileFailsBeforeDestinationCreation()
    {
        using var fixture = new MetadataBackupFixture();
        File.Delete(Path.Combine(fixture.BackupDirectory, MetadataTestContract.IdentityFileName));
        var destination = Path.Combine(fixture.BackupDirectory, MetadataTestContract.MissingFileRestorePath);

        var failure = Assert.ThrowsExactly<FileNotFoundException>(() =>
            ZoneTreeStore.Restore(fixture.BackupDirectory, destination, UnitExecutionOptions.StorageExecution()));

        await Assert.That(failure).IsNotNull();
        await Assert.That(Directory.Exists(destination)).IsFalse();
    }

    private static async Task AssertNullManifestFailure(MetadataBackupFixture fixture, string content)
    {
        await File.WriteAllTextAsync(Path.Combine(fixture.BackupDirectory,
            MetadataTestContract.ManifestFileName), content);
        var destination = Path.Combine(fixture.BackupDirectory, MetadataTestContract.NullManifestRestorePath);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            ZoneTreeStore.Restore(fixture.BackupDirectory, destination, UnitExecutionOptions.StorageExecution()));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(Directory.Exists(destination)).IsFalse();
    }

    private static async Task AssertMalformedManifestFailure(MetadataBackupFixture fixture, string content)
    {
        await File.WriteAllTextAsync(Path.Combine(fixture.BackupDirectory,
            MetadataTestContract.ManifestFileName), content);
        var destination = Path.Combine(fixture.BackupDirectory, MetadataTestContract.MalformedManifestRestorePath);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            ZoneTreeStore.Restore(fixture.BackupDirectory, destination, UnitExecutionOptions.StorageExecution()));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(Directory.Exists(destination)).IsFalse();
    }
}
