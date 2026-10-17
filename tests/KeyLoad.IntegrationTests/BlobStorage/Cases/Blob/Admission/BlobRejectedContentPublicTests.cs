namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "blob-rejected-content-public")]
[NotInParallel]
internal sealed class BlobRejectedContentPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task InvalidHashChangedDuplicateAndWrongChainRefuseThroughSdkAndOfficialMcpThenOriginalFailuresRemainAfterPublication()
    {
        await BlobRejectedContentPublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
        await BlobUnderfilledR47Trial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
    }
}
