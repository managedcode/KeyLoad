namespace KeyLoad.IntegrationTests.Features.Authorization;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "cluster")]
[NotInParallel]
internal sealed class CrossTenantClusterWholeFlowTests(ClusterFixture fixture)
{
    [Test]
    public Task ForeignWritesScansAndIndexesAreDeniedAndAuthorizedReceiptRemainsStable()
        => CrossTenantClusterWholeFlow.RunAsync(fixture);
}
