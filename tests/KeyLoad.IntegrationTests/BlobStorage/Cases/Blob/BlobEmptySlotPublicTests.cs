namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "blob-empty-slot-public")]
[NotInParallel]
internal sealed class BlobEmptySlotPublicTests(ClusterFixture fixture)
{
    [Test]
    public Task EmptyUploadConsumesVersionUntilAbortAndZeroPartReclaimThenFreshPublicationPreservesOriginalFailures()
        => BlobEmptySlotPublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
