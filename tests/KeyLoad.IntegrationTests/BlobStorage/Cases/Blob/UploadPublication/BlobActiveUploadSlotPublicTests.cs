namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "blob-active-upload-slot-public")]
[NotInParallel]
internal sealed class BlobActiveUploadSlotPublicTests(ClusterFixture fixture)
{
    [Test]
    public Task ZeroByteActiveUploadLimitAggregatesPartitionKeysSeparatesResourcesAndOriginalFailureSurvivesRepair()
        => BlobActiveUploadSlotPublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
