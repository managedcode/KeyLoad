namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class MaintenanceLifetimeTests
{
    [Test]
    public async Task AcStorageMaintenanceJoin001ObservedDiskMergeSettlesBeforeOwnerFilesAndHealthyReopen()
        => await MaintenanceJoinFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
