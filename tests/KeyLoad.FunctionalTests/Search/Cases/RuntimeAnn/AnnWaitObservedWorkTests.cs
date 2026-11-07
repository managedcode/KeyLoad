using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class AnnWaitObservedWorkTests
{

    [Test]
    public Task ActualAcquisitionCancellationSettlesNoPartialThenSamePrefixIsHealthy()
    {
        return AnnWaitWholeFlow.RunAsync(async (fixture, runtime, pin, receipt) =>
        {
            var database = fixture.Canonical;
            var request = AnnWaitWholeFlow.Request(database, pin, receipt);
            var image = AnnMaintenanceTestData.Snapshot(database);
            var cut = database.Store.Position;
            var before = database.Store.GetReadDiagnostics();
            using var caller = new CancellationTokenSource();
            using var observed = new ReadObservedStore(database.Store);
            var engine = new SearchEngine(database.CreateConfiguredDatabase(observed),
                UnitExecutionOptions.QueryExecution(new() { EnableApproximateSearch = true }), null, runtime.Owner);
            observed.CancelAfterReadProgress(caller, () => database.Store.GetReadDiagnostics().RangeExaminedBytes);
            WaitForAnnIndexResult? partial = null;
            var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => partial =
                await engine.WaitForAnnIndexAsync(AnnWaitWholeFlow.Root, request, caller.Token))
                ?? throw new InvalidOperationException();
            await Assert.That(error.CancellationToken).IsEqualTo(caller.Token);
            await Assert.That(observed.CancelledAfterProgress).IsTrue();
            await Assert.That(database.Store.GetReadDiagnostics().RangeExaminedBytes).IsGreaterThan(before.RangeExaminedBytes);
            await Assert.That(partial).IsNull();
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            var healthy = await engine.WaitForAnnIndexAsync(AnnWaitWholeFlow.Root, request,
                TestContext.Current!.Execution.CancellationToken);
            var expected = runtime.VerifiedCanonicalCut ?? throw new InvalidOperationException();
            await Assert.That(healthy.IndexedToken).IsEqualTo(expected);
            await Assert.That(expected.Position).IsGreaterThanOrEqualTo(request.MinimumToken.Position);
            await Assert.That(healthy.IndexGeneration).IsEqualTo(request.IndexGeneration);
            await Assert.That(healthy.SchemaVersion).IsEqualTo(AnnWaitWholeFlow.FirstPosition);
            await Assert.That(healthy.PolicyEpoch).IsEqualTo(AnnWaitWholeFlow.FirstPosition);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        }, new DatabaseLimits());
    }
}
