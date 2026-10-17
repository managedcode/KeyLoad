namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "blob-row-creator-public")]
[NotInParallel]
internal sealed class BlobRowCreatorPublicTests(ClusterFixture fixture)
{
    [Test]
    public Task CurrentRowOwnerRepairExposesReadsWithoutTransferringUploadCreatorAcrossSdkAndOfficialMcp()
        => BlobRowCreatorPublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
