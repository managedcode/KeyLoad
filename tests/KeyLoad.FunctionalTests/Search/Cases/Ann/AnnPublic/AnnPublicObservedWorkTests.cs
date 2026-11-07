using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class AnnPublicObservedWorkTests
{
    private const int Threshold = 3;
    private const int Breadth = 3;

    [Test]
    public Task ActualAuthorizedReadWorkCancelsWithoutPartialAndReleasesLeaseBeforeHealthy()
    {
        return AnnPublicWholeFlow.RunAsync(Threshold, Breadth, async (database, runtime, pin) =>
        {
            using var cancellation = new CancellationTokenSource();
            var request = AnnPublicWholeFlow.Request(database, pin);
            var image = AnnMaintenanceTestData.Snapshot(database);
            var cut = database.Store.Position;
            var files = AnnFixtureFileSnapshot.Capture(database.Directory);
            var before = database.Store.GetReadDiagnostics().RangeExaminedBytes;
            var triggered = false;
            using var observed = new ReadObservedStore(database.Store, afterRange: () =>
            {
                if (!triggered && database.Store.GetReadDiagnostics().RangeExaminedBytes > before)
                {
                    triggered = true;
                    cancellation.Cancel();
                }
            });
            var engine = new SearchEngine(database.CreateConfiguredDatabase(observed),
                UnitExecutionOptions.QueryExecution(new() { EnableApproximateSearch = true }), null, runtime.Owner);
            AnnSearchPage? partial = null;
            var failure = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
                partial = await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request, cancellation.Token))
                ?? throw new InvalidOperationException("The real execution public read did not cancel.");
            await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
            await Assert.That(triggered).IsTrue();
            await Assert.That(database.Store.GetReadDiagnostics().RangeExaminedBytes).IsGreaterThan(before);
            await Assert.That(partial).IsNull();
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            await Assert.That(AnnFixtureFileSnapshot.Capture(database.Directory))
                .IsEquivalentTo(files, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await AnnPublicWholeFlow.PageAsync(await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request),
                database, cut, AnnPageMode.Exact);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        });
    }
}
