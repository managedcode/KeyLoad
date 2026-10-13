namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "blob-scoped-upload-identity-public")]
[NotInParallel]
internal sealed class BlobScopedUploadIdentityPublicTests(ClusterFixture fixture)
{
    [Test]
    public Task SameUploadIdRemainsScopedAcrossPartitionResourceAndNameWhileSingleLifetimeCleanupPreservesOtherReceipts()
        => BlobScopedUploadIdentityPublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
