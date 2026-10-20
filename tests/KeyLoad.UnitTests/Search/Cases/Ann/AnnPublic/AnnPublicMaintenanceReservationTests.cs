using KeyLoad.Clustering;
using KeyLoad.Query;
using KeyLoad.Server;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnPublicMaintenanceReservationTests
{
    private const int Threshold = 3;
    private const int Breadth = 3;
    private const string Bound = "The execution ANN generation reservation is exceeded.";
    private const string Missing = "The actual concurrent execution public read did not reject its resident admission.";

    [Test]
    public Task ActualMaintenanceReadReservationRejectsConcurrentPublicFrameThenSettledStageReturnsLiteralHealthyPage()
    {
        var clock = new AnnPublicObservedWorkClock();
        return AnnPublicWholeFlow.RunAsync(Threshold, Breadth, async (database, runtime, pin) =>
        {
            var request = AnnPublicWholeFlow.Request(database, pin);
            var engine = AnnPublicWholeFlow.Engine(database, runtime);
            var image = AnnMaintenanceTestData.Snapshot(database);
            var cut = database.Store.Position;
            var files = AnnFixtureFileSnapshot.Capture(database.Directory);
            var before = database.Store.GetReadDiagnostics().RangeExaminedBytes;
            var token = TestContext.Current!.Execution.CancellationToken;
            var failure = await RejectWhileVerifyingAsync(database, runtime, pin, engine, request, clock, before, token);
            await Assert.That(clock.Triggered).IsTrue();
            await Assert.That(database.Store.GetReadDiagnostics().RangeExaminedBytes).IsGreaterThan(before);
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
            await Assert.That(failure.Message).IsEqualTo(Bound);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            await Assert.That(AnnFixtureFileSnapshot.Capture(database.Directory)).IsEquivalentTo(files, CollectionOrdering.Matching);
            await AnnPublicWholeFlow.PageAsync(await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal,
                request, token), database, cut, AnnPageMode.Exact);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        }, clock);
    }

    private static async Task<KeyLoadException> RejectWhileVerifyingAsync(TestDatabase database,
        AnnMaintenanceTestRuntime runtime, AnnMaintenanceRequest pin, SearchEngine engine,
        ApproximateSearchRequest request, AnnPublicObservedWorkClock clock, long before, CancellationToken token)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        clock.Arm(() => runtime.Owner.ObserveWork(runtime.SessionId)?.Stage == AnnMaintenanceCapabilityKind.Verify
            && database.Store.GetReadDiagnostics().RangeExaminedBytes > before,
            () => { entered.TrySetResult(); release.Wait(token); });
        var failures = new List<Exception>();
        Task<AnnMaintenanceCapabilityResult>? original = null;
        KeyLoadException? rejected = null;
        AnnSearchPage? partial = null;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                original = Task.Run(() => runtime.PhaseAsync(database, pin, AnnMaintenanceCapabilityKind.Verify,
                    token: token), CancellationToken.None);
                await entered.Task.WaitAsync(token);
                rejected = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial =
                    await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request, token));
                await Assert.That(partial).IsNull();
            }, failures);
        }
        finally
        {
            release.Set();
            clock.Disarm();
            if (original is not null)
            {
                await ServerFailureObserver.ObserveAsync(async () => _ = await original, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return rejected ?? throw new InvalidOperationException(Missing);
    }

}
