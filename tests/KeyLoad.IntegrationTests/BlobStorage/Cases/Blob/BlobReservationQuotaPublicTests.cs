namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "blob-reservation-quota-public")]
[NotInParallel]
internal sealed class BlobReservationQuotaPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task ExactReservationQuotaRefusesThenAbortReleasesAndFreshPublicationPreservesOriginalFailureReplay()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await BlobReservationQuotaPublicTrial.RunAsync(fixture, token);
        await BlobGlobalQuotaR49Trial.RunAsync(fixture, token);
    }
}
