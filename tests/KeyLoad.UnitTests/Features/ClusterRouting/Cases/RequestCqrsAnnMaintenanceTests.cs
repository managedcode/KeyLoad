namespace KeyLoad.UnitTests.Features.ClusterRouting;

[RequestCqrsDataSource(nativeAnnMaintenance: true)]
[NotInParallel]
internal sealed class RequestCqrsAnnMaintenanceTests(RequestCqrsClusterFixture fixture)
{
    [Test]
    public Task SignedNativeAnnBuildRestoreCarriesEveryPhaseLiteralSearchAndOriginalCheckpointReplay()
        => RequestCqrsAnnWholeFlow.RunAsync(fixture, malformed: false);

    [Test]
    public Task SignedNativeAnnMalformedFramesJoinWithoutEffectsThenSameConnectionRestoresAndReplays()
        => RequestCqrsAnnWholeFlow.RunAsync(fixture, malformed: true);
}
