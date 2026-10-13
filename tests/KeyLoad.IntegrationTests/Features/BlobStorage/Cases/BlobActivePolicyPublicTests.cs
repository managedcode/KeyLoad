namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "blob-active-policy-public")]
[NotInParallel]
internal sealed class BlobActivePolicyPublicTests(ClusterFixture fixture)
{
    [Test]
    public Task ActiveUploadWriteRevocationHigherEpochRepairFencesOriginalIdsAndPreservesFreshPublicationAcrossTwoRf3ColdCycles()
        => BlobActivePolicyPublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
