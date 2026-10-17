namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NativeMaintenanceLifetimeTests
{
    [Test]
    public async Task AcStorageMaintenanceJoin001ObservedNativeDiskMergeSettlesBeforeOwnerFilesAndHealthyReopen()
        => await NativeMaintenanceJoinFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
