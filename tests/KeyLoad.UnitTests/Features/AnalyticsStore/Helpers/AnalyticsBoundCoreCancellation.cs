using KeyLoad.Core;
using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestData;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsBoundCoreCancellation
{
    private static readonly TimeSpan ShortOriginalExpiry = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BoundedObservation = TimeSpan.FromMilliseconds(1_100);

    internal static Task CallerAsync(CancellationToken token)
        => AnalyticsBoundCoreOwner.RunAsync(async (fixture, owner, original) =>
        {
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(original);
            await AnalyticsBoundCoreOwner.LeaseAsync(fixture, owner, async (lease, ledger, work) =>
                await AnalyticsBoundsNative.UnchangedAsync(fixture, async () =>
                {
                    caller.Cancel();
                    var error = Assert.ThrowsExactly<OperationCanceledException>(() =>
                        fixture.Database.CaptureAnalyticsAggregateBound(D.Root, D.Query(fixture), work, lease));
                    await Assert.That(error.CancellationToken).IsEqualTo(caller.Token);
                    await Assert.That(original.IsCancellationRequested).IsFalse();
                    await Assert.That(ledger.RetainedEntries).IsEqualTo(0);
                }), caller.Token);
            await AnalyticsBoundCoreFlow.HealthyAsync(fixture, owner, original);
        }, token);

    internal static Task DeadlineAsync(CancellationToken token)
        => AnalyticsBoundCoreOwner.RunAsync(async (fixture, owner, original) =>
        {
            await AnalyticsBoundCoreOwner.LeaseAsync(fixture, owner, async (lease, ledger, work) =>
                await AnalyticsBoundsNative.UnchangedAsync(fixture, async () =>
                {
                    work.ConstrainLifetime(fixture.Database.EvaluationClock.GetUtcNow() + ShortOriginalExpiry);
                    await Task.Delay(BoundedObservation, fixture.Database.EvaluationClock, original);
                    var error = Assert.ThrowsExactly<KeyLoadException>(() =>
                        fixture.Database.CaptureAnalyticsAggregateBound(D.Root, D.Query(fixture), work, lease));
                    await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
                    await Assert.That(original.IsCancellationRequested).IsFalse();
                    await Assert.That(ledger.RetainedEntries).IsEqualTo(0);
                }), original);
            await AnalyticsBoundCoreFlow.HealthyAsync(fixture, owner, original);
        }, token);
}
