namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PhysicalOwnerDirectoryReadAuthorityFlowTests
{
    [Test]
    public async Task RevokedDirectoryAdministratorRefusesBeforeCorruptAuthorityThenExactRepairGrantReplayAndColdRemainHealthy()
    {
        var failures = new List<Exception>();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new PhysicalShardCatalogFixture();
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                () => PhysicalOwnerDirectoryReadAuthorityFlow.RunAsync(fixture), failures);
        }, failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
