namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class AtomicPartitionPlacementAuthorityFlowTests
{
    [Test]
    public async Task MalformedPlacementRefusesBeforeOwnerLookupThenSameGuidRepairsReplaysColdAndContinues()
    {
        var failures = new List<Exception>();
        PhysicalShardCatalogFixture? fixture = null;
        try
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                fixture = new PhysicalShardCatalogFixture();
                await AtomicPartitionPlacementAuthorityFlow.RunAsync(fixture);
            }, failures);
        }
        finally
        {
            if (fixture is not null)
            { KeyLoad.Server.ServerFailureObserver.Observe(fixture.Dispose, failures); }
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
