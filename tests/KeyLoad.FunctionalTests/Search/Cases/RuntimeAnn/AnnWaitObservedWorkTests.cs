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
            await using var observed = new ObservedWorkCancellation(database.Store, caller,
                static progress => progress.RangeExaminedBytes);
            await observed.Ready;
            WaitForAnnIndexResult? partial = null;
            var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => partial =
                await AnnPublicWholeFlow.Engine(database, runtime).WaitForAnnIndexAsync(AnnWaitWholeFlow.Root, request, caller.Token))
                ?? throw new InvalidOperationException();
            await Assert.That(error.CancellationToken).IsEqualTo(caller.Token);
            await Assert.That(observed.CancelledAfterProgress).IsTrue();
            await Assert.That(partial).IsNull();
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            await AnnWaitWholeFlow.HealthyAsync(database, runtime, request, TestContext.Current!.Execution.CancellationToken);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        }, new DatabaseLimits());
    }
}
