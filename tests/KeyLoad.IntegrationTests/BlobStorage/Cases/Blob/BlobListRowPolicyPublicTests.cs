namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "blob-list-row-policy-public")]
[NotInParallel]
internal sealed class BlobListRowPolicyPublicTests(ClusterFixture fixture)
{
    [Test]
    public Task BoundedListingReloadsRowOwnerAndGrantsForExclusiveCursorThenPreservesOriginalReceipts()
        => BlobListRowPolicyPublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
