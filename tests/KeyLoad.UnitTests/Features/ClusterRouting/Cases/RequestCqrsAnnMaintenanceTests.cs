namespace KeyLoad.UnitTests.Features.ClusterRouting;

[RequestCqrsHostedDataSource(nativeAnnMaintenance: true)]
internal sealed class RequestCqrsAnnMaintenanceTests(RequestCqrsHostedRuntimeFixture fixture)
{
    [Test]
    public Task SignedNativeAnnBuildRestoreCarriesEveryPhaseLiteralSearchAndOriginalCheckpointReplay()
        => RequestCqrsAnnWholeFlow.RunAsync(fixture, malformed: false);

    [Test]
    public Task SignedNativeAnnMalformedFramesJoinWithoutEffectsThenSameConnectionRestoresAndReplays()
        => RequestCqrsAnnWholeFlow.RunAsync(fixture, malformed: true);
}
