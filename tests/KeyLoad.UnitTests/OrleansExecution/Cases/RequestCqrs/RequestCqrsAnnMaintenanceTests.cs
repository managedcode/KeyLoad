namespace KeyLoad.UnitTests.OrleansExecution;

[RequestCqrsHostedDataSource(annMaintenance: true)]
internal sealed class RequestCqrsAnnMaintenanceTests(RequestCqrsHostedRuntimeFixture fixture)
{
    [Test]
    public Task SignedAnnBuildRestoreCarriesEveryPhaseLiteralSearchAndOriginalCheckpointReplay()
        => RequestCqrsAnnWholeFlow.RunAsync(fixture, malformed: false);

    [Test]
    public Task SignedAnnMalformedFramesJoinWithoutEffectsThenSameConnectionRestoresAndReplays()
        => RequestCqrsAnnWholeFlow.RunAsync(fixture, malformed: true);
}
