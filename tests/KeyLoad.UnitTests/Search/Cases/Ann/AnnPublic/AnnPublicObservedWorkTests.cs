namespace KeyLoad.UnitTests.Search;

internal sealed class AnnPublicObservedWorkTests
{
    private const int Threshold = 3;
    private const int Breadth = 3;
    private const string Deadline = "The read execution deadline is exceeded.";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ActualAuthorizedReadWorkCancelsOrExpiresWithoutPartialAndReleasesLeaseBeforeHealthy(bool cancel)
    {
        var clock = new AnnPublicObservedWorkClock();
        return AnnPublicWholeFlow.RunAsync(Threshold, Breadth, async (database, runtime, pin) =>
        {
            using var cancellation = new CancellationTokenSource();
            var request = AnnPublicWholeFlow.Request(database, pin);
            var engine = AnnPublicWholeFlow.Engine(database, runtime);
            var image = AnnMaintenanceTestData.Snapshot(database);
            var cut = database.Store.Position;
            var files = AnnFixtureFileSnapshot.Capture(database.Directory);
            var before = database.Store.GetReadDiagnostics().RangeExaminedBytes;
            clock.Arm(() => database.Store.GetReadDiagnostics().RangeExaminedBytes > before,
                () =>
                {
                    if (cancel)
                    {
                        cancellation.Cancel();
                    }
                    else
                    {
                        clock.Advance(TimeSpan.FromSeconds(database.Database.Limits.QueryDeadlineSeconds + 1));
                    }
                });
            AnnSearchPage? partial = null;
            try
            {
                if (cancel)
                {
                    var failure = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
                        partial = await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request, cancellation.Token))
                        ?? throw new InvalidOperationException("The real execution public read did not cancel.");
                    await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
                }
                else
                {
                    var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
                        partial = await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request, cancellation.Token))
                        ?? throw new InvalidOperationException("The real execution public read did not expire.");
                    await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
                    await Assert.That(failure.Message).IsEqualTo(Deadline);
                }
            }
            finally { clock.Disarm(); }
            await Assert.That(clock.Triggered).IsTrue();
            await Assert.That(database.Store.GetReadDiagnostics().RangeExaminedBytes).IsGreaterThan(before);
            await Assert.That(partial).IsNull();
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            await Assert.That(AnnFixtureFileSnapshot.Capture(database.Directory))
                .IsEquivalentTo(files, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await AnnPublicWholeFlow.PageAsync(await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request),
                database, cut, AnnPageMode.Exact);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        }, clock);
    }
}
