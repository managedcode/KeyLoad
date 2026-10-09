namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NativeMaintenancePeriodicLifetimeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcStorageMaintenanceJoin002003ActualPeriodicCleanupJoinsBeforeOwnerFilesAndHealthyReopen(bool failSweep)
        => await NativePeriodicMaintenanceFlow.RunAsync(failSweep, TestContext.Current!.Execution.CancellationToken);
}
