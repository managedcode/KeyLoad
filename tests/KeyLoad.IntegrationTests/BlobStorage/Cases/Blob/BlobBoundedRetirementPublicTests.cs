namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "blob-bounded-retirement-public")]
[NotInParallel]
internal sealed class BlobBoundedRetirementPublicTests(ClusterFixture fixture)
{
    [Test]
    public Task TwoPartRetirementReclaimsOnePartPerCommandPreservesReplayAndPublishesNewLifetime()
        => BlobBoundedRetirementPublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
