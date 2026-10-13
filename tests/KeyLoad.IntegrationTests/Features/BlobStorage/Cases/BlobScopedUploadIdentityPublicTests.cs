namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "blob-scoped-upload-identity-public")]
[NotInParallel]
internal sealed class BlobScopedUploadIdentityPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task SameUploadIdRemainsScopedAcrossPartitionResourceAndNameWhileSingleLifetimeCleanupPreservesOtherReceipts()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await BlobScopedUploadIdentityPublicTrial.RunAsync(fixture, token);
        await BlobScopedBytesR42Trial.RunAsync(fixture, token);
    }
}
