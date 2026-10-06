using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class MetadataFormatFailureTests
{
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
}
