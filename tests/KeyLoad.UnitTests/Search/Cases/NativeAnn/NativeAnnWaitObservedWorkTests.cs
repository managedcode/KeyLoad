using KeyLoad.UnitTests.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeAnnWaitObservedWorkTests
{
    private const int DeadlineSeconds = 1;
    private const int ExpiredSeconds = 2;
    private const string DeadlineDetail = "The read execution deadline is exceeded.";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ActualNativeAcquisitionCancellationOrDeadlineSettlesNoPartialThenSamePrefixIsHealthy(bool cancel)
    {
        var clock = new QueryObservedWorkClock();
        return NativeAnnWaitWholeFlow.RunAsync(async (fixture, runtime, pin, receipt) =>
        {
            var database = fixture.Canonical;
            var request = NativeAnnWaitWholeFlow.Request(database, pin, receipt);
            var image = NativeAnnMaintenanceTestData.Snapshot(database);
            var cut = database.Store.Position;
            var before = database.Store.GetReadDiagnostics();
            using var caller = new CancellationTokenSource();
            clock.Arm(() => database.Store.GetReadDiagnostics().RangeExaminedBytes > before.RangeExaminedBytes,
                () => { if (cancel) { caller.Cancel(); } else { clock.Advance(TimeSpan.FromSeconds(ExpiredSeconds)); } });
            WaitForAnnIndexResult? partial = null;
            try
            {
                if (cancel)
                {
                    var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => partial =
                        await AnnPublicWholeFlow.Engine(database, runtime).WaitForAnnIndexAsync(NativeAnnWaitWholeFlow.Root, request, caller.Token))
                        ?? throw new InvalidOperationException();
                    await Assert.That(error.CancellationToken).IsEqualTo(caller.Token);
                }
                else
                {
                    var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial =
                        await AnnPublicWholeFlow.Engine(database, runtime).WaitForAnnIndexAsync(NativeAnnWaitWholeFlow.Root, request, caller.Token))
                        ?? throw new InvalidOperationException();
                    await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
                    await Assert.That(error.Message).IsEqualTo(DeadlineDetail);
                }
            }
            finally { clock.Disarm(); }
            await Assert.That(clock.Triggered).IsTrue();
            await Assert.That(partial).IsNull();
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            await NativeAnnWaitWholeFlow.HealthyAsync(database, runtime, request, TestContext.Current!.Execution.CancellationToken);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        }, new DatabaseLimits { QueryDeadlineSeconds = DeadlineSeconds }, clock);
    }
}
