namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "blob-object-key-lifetime-public")]
[NotInParallel]
internal sealed class BlobObjectKeyLifetimePublicTests(ClusterFixture fixture)
{
    [Test]
    public Task PermanentObjectKeySurvivesEmptyReclaimWhileSameNameAndSeparateResourceRemainHealthy()
        => BlobObjectKeyLifetimePublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
