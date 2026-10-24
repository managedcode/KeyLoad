namespace KeyLoad.UnitTests.Search;

internal sealed class AnnPublicReservationTests
{
    private const int Threshold = 3;
    private const int Breadth = 3;
    private const string Denied = "ann-reservation-no-grant";

    [Test]
    public Task RealPublishedOwnerCountsReceiptAndIndexBeforeSaturatingSplittingAndReleasingPublicReservations()
        => AnnPublicReservationFlow.RunAsync();

    [Test]
    public Task RealPublicSearchStillChecksCurrentPrincipalAndReturnsOriginalCompletePageWithoutCanonicalEffects()
        => AnnPublicWholeFlow.RunAsync(Threshold, Breadth, async (database, runtime, pin) =>
        {
            AnnSeedTestSupport.Persist(database, Denied, Capability.None, []);
            var image = AnnMaintenanceTestData.Snapshot(database);
            var cut = database.Store.Position;
            var engine = AnnPublicWholeFlow.Engine(database, runtime);
            var request = AnnPublicWholeFlow.Request(database, pin);
            AnnSearchPage? partial = null;
            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial =
                await engine.ApproximateSearchAsync(Denied, request, TestContext.Current!.Execution.CancellationToken))
                ?? throw new InvalidOperationException("The actual persisted no-grant principal was not refused.");
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(partial).IsNull();
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            var actual = await engine.ApproximateSearchAsync(AnnWaitWholeFlow.Root, request,
                TestContext.Current!.Execution.CancellationToken);
            await AnnPublicWholeFlow.PageAsync(actual, database, cut, AnnPageMode.Exact);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        });
}
