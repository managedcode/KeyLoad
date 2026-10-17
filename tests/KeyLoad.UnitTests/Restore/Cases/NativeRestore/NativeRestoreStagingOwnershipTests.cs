namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class NativeRestoreStagingOwnershipTests
{
    [Test]
    public async Task NativeCatalogAcquisitionRefusalRetainsOriginalStagingThenRepairAndHealthyTwoCold()
        => await NativeRestoreStagingOwnershipFlow.RunAsync(
            TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
}
