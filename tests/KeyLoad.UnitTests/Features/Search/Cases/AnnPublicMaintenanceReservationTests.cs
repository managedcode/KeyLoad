using KeyLoad.Orleans;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnPublicMaintenanceReservationTests
{
    private const int Threshold = 3;
    private const int Breadth = 3;
    private const string Bound = "The native ANN generation reservation is exceeded.";
    private const string Missing = "The actual concurrent native public read did not reject its resident admission.";

    [Test]
    public Task ActualMaintenanceReadReservationRejectsConcurrentPublicFrameThenSettledStageReturnsLiteralHealthyPage()
    {
        var clock = new AnnPublicObservedWorkClock();
        return AnnPublicWholeFlow.RunAsync(Threshold, Breadth, async (database, runtime, pin) =>
        {
            var request = AnnPublicWholeFlow.Request(database, pin);
            var engine = AnnPublicWholeFlow.Engine(database, runtime);
            var image = NativeAnnMaintenanceTestData.Snapshot(database);
            var cut = database.Store.Position;
            var files = NativeAnnFixtureFileSnapshot.Capture(database.Directory);
            var before = database.Store.GetReadDiagnostics().RangeExaminedBytes;
            var token = TestContext.Current!.Execution.CancellationToken;
            KeyLoadException? rejected = null;
            AnnSearchPage? partial = null;
            clock.Arm(() => runtime.Owner.ObserveWork(runtime.SessionId)?.Stage == AnnMaintenanceCapabilityKind.Verify
                && database.Store.GetReadDiagnostics().RangeExaminedBytes > before,
                () =>
                {
                    try
                    {
                        partial = engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request, token)
                        .WaitAsync(token).GetAwaiter().GetResult();
                    }
                    catch (KeyLoadException error) { rejected = error; }
                });
            try
            { _ = await runtime.PhaseAsync(database, pin, AnnMaintenanceCapabilityKind.Verify, token: token); }
            finally { clock.Disarm(); }
            await Assert.That(clock.Triggered).IsTrue();
            await Assert.That(database.Store.GetReadDiagnostics().RangeExaminedBytes).IsGreaterThan(before);
            var failure = rejected ?? throw new InvalidOperationException(Missing);
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
            await Assert.That(failure.Message).IsEqualTo(Bound);
            await Assert.That(partial).IsNull();
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            await Assert.That(NativeAnnFixtureFileSnapshot.Capture(database.Directory)).IsEquivalentTo(files, CollectionOrdering.Matching);
            await AnnPublicWholeFlow.PageAsync(await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal,
                request, token), database, cut, AnnPageMode.Exact);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        }, clock);
    }
}
