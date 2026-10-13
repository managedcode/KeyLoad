namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "blob-read-policy-public")]
[NotInParallel]
internal sealed class BlobReadPolicyPublicTests(ClusterFixture fixture)
{
    [Test]
    public Task PersistedReadGrantRemovalAndRepairControlMetadataAndEveryRangeOnOriginalPublicSessions()
        => BlobReadPolicyPublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
