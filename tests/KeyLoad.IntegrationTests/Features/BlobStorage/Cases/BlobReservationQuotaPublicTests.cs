namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "blob-reservation-quota-public")]
[NotInParallel]
internal sealed class BlobReservationQuotaPublicTests(ClusterFixture fixture)
{
    [Test]
    public Task ExactReservationQuotaRefusesThenAbortReleasesAndFreshPublicationPreservesOriginalFailureReplay()
        => BlobReservationQuotaPublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
