using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal sealed class MetadataAllocationTests
{
    [Test]
    public async Task OversizedManifestRestoreAllocatesLessThanHalfItsInput()
    {
        using var fixture = new MetadataBackupFixture();
        var manifestPath = Path.Combine(fixture.BackupDirectory, MetadataTestContract.ManifestFileName);
        await MetadataTestFiles.PadWithTrailingBytesAsync(manifestPath, MetadataTestContract.OversizedMetadataBytes);

        var destination = MetadataTestContract.OversizedManifestRestorePath(fixture.RestoredDirectory);
        var inputLength = new FileInfo(manifestPath).Length;
        await AssertRejectedRestore((RejectRestore(fixture.BackupDirectory, destination)).Value,
            MetadataTestContract.ManifestUnsupportedDetail);
        await Assert.That(Directory.Exists(destination)).IsFalse();

        var (failure, allocatedBytes) = MeasureRejectedRestore(fixture.BackupDirectory, destination);

        await AssertRejectedRestore(failure, MetadataTestContract.ManifestUnsupportedDetail);
        await Assert.That(Directory.Exists(destination)).IsFalse();
        await Assert.That(allocatedBytes).IsLessThan(inputLength / 2);
    }

    [Test]
    public async Task OversizedIdentityRestoreAllocatesLessThanHalfItsInput()
    {
        using var fixture = new MetadataBackupFixture();
        var identityPath = Path.Combine(fixture.BackupDirectory, MetadataTestContract.IdentityFileName);
        await MetadataTestFiles.PadWithTrailingBytesAsync(identityPath, MetadataTestContract.OversizedMetadataBytes);
        await MetadataTestFiles.UpdateManifestFileAsync(fixture.BackupDirectory, MetadataTestContract.IdentityFileName);

        var destination = MetadataTestContract.OversizedIdentityRestorePath(fixture.RestoredDirectory);
        var inputLength = new FileInfo(identityPath).Length;
        await AssertRejectedRestore((RejectRestore(fixture.BackupDirectory, destination)).Value,
            MetadataTestContract.IdentityFormatUnsupportedDetail);
        await Assert.That(Directory.Exists(destination)).IsFalse();

        var (failure, allocatedBytes) = MeasureRejectedRestore(fixture.BackupDirectory, destination);

        await AssertRejectedRestore(failure, MetadataTestContract.IdentityFormatUnsupportedDetail);
        await Assert.That(Directory.Exists(destination)).IsFalse();
        await Assert.That(allocatedBytes).IsLessThan(inputLength / 2);
    }

    private static (bool HasValue, KeyLoadException? Value) RejectRestore(string backup, string destination)
    {
        try
        {
            ZoneTreeStore.Restore(backup, destination, UnitExecutionOptions.StorageExecution());
        }
        catch (KeyLoadException error)
        {
            return (true, ((KeyLoadException?)(error)));
        }

        return (false, default(KeyLoadException?));
    }

    private static (KeyLoadException? Failure, long AllocatedBytes) MeasureRejectedRestore(string backup, string destination)
    {
        var start = GC.GetAllocatedBytesForCurrentThread();
        var failure = (RejectRestore(backup, destination)).Value;
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - start;

        return (failure, allocatedBytes);
    }

    private static async Task AssertRejectedRestore(KeyLoadException? failure, string detail)
    {
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(failure.Message).IsEqualTo(detail);
    }
}
