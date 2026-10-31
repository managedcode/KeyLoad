using D = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsStoreTestData;

namespace KeyLoad.FunctionalTests.AnalyticsStore;

internal static class AnalyticsBoundCoreCancellation
{
    internal static Task CallerAsync(CancellationToken token)
        => AnalyticsBoundCoreOwner.RunAsync(async (fixture, owner, original) =>
        {
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(original);
            await AnalyticsBoundCoreOwner.LeaseAsync(fixture, owner, async (lease, ledger, work) =>
                await RuntimeAnalyticsBounds.UnchangedAsync(fixture, async () =>
                {
                    await caller.CancelAsync();
                    var error = Assert.ThrowsExactly<OperationCanceledException>(() =>
                        fixture.Database.CaptureAnalyticsAggregateBound(D.Root, D.Query(fixture), work, lease));
                    await Assert.That(error.CancellationToken).IsEqualTo(caller.Token);
                    await Assert.That(original.IsCancellationRequested).IsFalse();
                    await Assert.That(ledger.RetainedEntries).IsEqualTo(0);
                }), caller.Token);
            await AnalyticsBoundCoreFlow.HealthyAsync(fixture, owner, original);
        }, token);
}
