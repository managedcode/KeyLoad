namespace KeyLoad.IntegrationTests.Features.Authorization;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "rf3")]
[NotInParallel]
internal sealed class CrossTenantRf3WholeFlowTests(ClusterFixture fixture)
{
    [Test]
    public Task Kl015ForeignWritesScansAndIndexesAreDeniedAndAuthorizedReceiptRemainsStable()
        => CrossTenantRf3WholeFlow.RunAsync(fixture);
}
