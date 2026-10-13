namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "blob-partial-boundary-public")]
[NotInParallel]
internal sealed class BlobPartialReadBoundaryPublicTests(ClusterFixture fixture)
{
    [Test]
    public Task ExactEndZeroAndBeyondLengthRefusalsPreserveHealthyPublicRangesAndOriginalReceipts()
        => BlobPartialReadBoundaryPublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
