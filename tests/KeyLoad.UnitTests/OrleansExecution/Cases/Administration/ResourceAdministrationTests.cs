namespace KeyLoad.UnitTests.Features.OrleansExecution;

[NotInParallel]
internal sealed class ResourceAdministrationTests
{
    [Test]
    public Task OriginalSignedTableDeletionReloadsPersistedAdministratorBeforeEffectsThenFreshRepairRemainsHealthy()
        => ResourceAdministrationFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
