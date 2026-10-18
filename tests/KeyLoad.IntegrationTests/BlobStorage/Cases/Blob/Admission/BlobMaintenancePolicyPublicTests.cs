namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "blob-maintenance-policy-public")]
[NotInParallel]
internal sealed class BlobMaintenancePolicyPublicTests(ClusterFixture fixture)
{
    [Test]
    public Task PersistedDeleteAndManageRevocationRepairPreservesRetiredBytesOriginalFailuresAndHealthyReceiptsAcrossTwoClusterColdCycles()
        => BlobMaintenancePolicyPublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
