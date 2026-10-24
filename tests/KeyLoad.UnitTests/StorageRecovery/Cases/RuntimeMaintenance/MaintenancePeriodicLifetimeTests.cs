namespace KeyLoad.UnitTests.StorageRecovery;

internal sealed class MaintenancePeriodicLifetimeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcStorageMaintenanceJoin002003OrleansTimerStopsBeforeOwnerFilesAndHealthyReopen(bool failSweep)
        => await PeriodicMaintenanceFlow.RunAsync(failSweep, TestContext.Current!.Execution.CancellationToken);
}
