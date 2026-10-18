namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ResourceAdministrationNativeTests
{
    [Test]
    public Task OriginalSignedTableDeletionReloadsPersistedAdministratorBeforeEffectsThenFreshRepairRemainsHealthy()
        => ResourceAdministrationNativeFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
