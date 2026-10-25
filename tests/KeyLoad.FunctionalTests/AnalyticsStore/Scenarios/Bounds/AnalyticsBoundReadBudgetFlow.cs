using D = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsStoreTestData;

namespace KeyLoad.FunctionalTests.AnalyticsStore;

internal static class AnalyticsBoundReadBudgetFlow
{
    private const int ExpectedRows = 1;
    private const long NoReadBytes = 0;
    private const int NoReadRecords = 0;

    internal static Task BytesAsync(CancellationToken token) => RunAsync(exhaustBytes: true, token);

    internal static Task RecordsAsync(CancellationToken token) => RunAsync(exhaustBytes: false, token);

    private static Task RunAsync(bool exhaustBytes, CancellationToken token)
        => AnalyticsBoundCoreOwner.RunAsync(async (fixture, owner, original) =>
        {
            await AnalyticsBoundCoreOwner.LeaseAsync(fixture, owner, async (lease, ledger, work) =>
                await RuntimeAnalyticsBounds.UnchangedAsync(fixture, async () =>
                {
                    // Activate execution accounting before real work, rather than inventing observed bytes or records.
                    work.ConstrainWork(work.RuntimeMaximumReadBytes, work.MaximumScanRecords);
                    var first = fixture.Database.ReadAnalyticsEventsBound(D.Root, D.Read(fixture), work, lease);
                    await Assert.That(first.Events.Length).IsEqualTo(ExpectedRows);
                    var bytes = work.ReadBytes;
                    var records = work.ConstrainedRecords;
                    var retained = ledger.RetainedEncodedBytes;
                    var dependencies = lease.Dependencies;
                    await Assert.That(bytes).IsGreaterThan(NoReadBytes);
                    await Assert.That(records).IsGreaterThan(NoReadRecords);
                    work.ConstrainWork(exhaustBytes ? bytes : work.RuntimeMaximumReadBytes,
                        exhaustBytes ? work.MaximumScanRecords : records);
                    AnalyticsEventsPage? page = null;
                    AnalyticsAggregateSnapshot? snapshot = null;
                    var error = Assert.ThrowsExactly<KeyLoadException>(() =>
                    {
                        if (exhaustBytes)
                        {
                            page = fixture.Database.ReadAnalyticsEventsBound(D.Root, D.Read(fixture), work, lease);
                        }
                        else
                        {
                            snapshot = fixture.Database.CaptureAnalyticsAggregateBound(D.Root, D.Query(fixture), work, lease);
                        }
                    });
                    await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
                    await Assert.That(page).IsNull();
                    await Assert.That(snapshot).IsNull();
                    await Assert.That(work.ReadBytes).IsEqualTo(bytes);
                    await Assert.That(work.ConstrainedRecords).IsEqualTo(records);
                    await Assert.That(ledger.RetainedEncodedBytes).IsEqualTo(retained);
                    await Assert.That(lease.Dependencies.SetEquals(dependencies)).IsTrue();
                }), original);
            await AnalyticsBoundCoreFlow.HealthyAsync(fixture, owner, original);
        }, token);
}
