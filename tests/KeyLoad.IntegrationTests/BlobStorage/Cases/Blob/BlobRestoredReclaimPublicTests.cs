using KeyLoad.IntegrationTests.Features.BackupRestore;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[NotInParallel]
internal sealed class BlobRestoredReclaimPublicTests
{
    [Test]
    public Task RestoredAbortedMultipartReclaimsOnePartAtATimePreservesExactCapacityAndTwoSixOwnerColdCuts()
        => ClusterRestoreRf3Tests.RunAsync(BlobRestoredReclaimPublicTrial.RunAsync);
}
