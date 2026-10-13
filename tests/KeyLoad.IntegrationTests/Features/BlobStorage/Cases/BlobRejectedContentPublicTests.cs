namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "blob-rejected-content-public")]
[NotInParallel]
internal sealed class BlobRejectedContentPublicTests(ClusterFixture fixture)
{
    [Test]
    public Task InvalidHashChangedDuplicateAndWrongChainRefuseThroughSdkAndOfficialMcpThenOriginalFailuresRemainAfterPublication()
        => BlobRejectedContentPublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
