using KeyLoad.IntegrationTests.Features.BackupRestore;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[NotInParallel]
internal sealed class BlobArchiveActivePublicTests
{
    /// <summary>Real owner archives retain multipart data and abandon old upload authority through repaired public reads and cold owners.</summary>
    [Test]
    public Task PublishedMultipartAndActiveReservationRestoreWithFencedReceiptsPolicyRepairAndTwoSixOwnerColdCuts()
        => ClusterRestoreRf3Tests.RunAsync(BlobArchiveActivePublicTrial.RunAsync);
}
