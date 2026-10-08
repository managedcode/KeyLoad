using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnPublicWholeFlowTests
{
    private const int ExactThreshold = 3;
    private const int ApproximateThreshold = 0;
    private const int FullBreadth = 3;
    private const int FallbackBreadth = 1;
    private const int TooSmallResult = 1;
    private const long MissingGeneration = 2;

    [Test]
    [Arguments(ExactThreshold, FullBreadth, AnnPageMode.Exact)]
    [Arguments(ApproximateThreshold, FullBreadth, AnnPageMode.Approximate)]
    [Arguments(ApproximateThreshold, FallbackBreadth, AnnPageMode.ExactFallback)]
    public Task ActualProvisionedSearchReportsRequestedAndNativeModeWithFullLiteralPageAndEmptyCut(int threshold, int breadth, AnnPageMode mode)
        => AnnPublicWholeFlow.RunAsync(threshold, breadth, async (database, runtime, pin) =>
        {
            var image = NativeAnnMaintenanceTestData.Snapshot(database);
            var cut = database.Store.Position;
            var request = AnnPublicWholeFlow.Request(database, pin);
            var engine = AnnPublicWholeFlow.Engine(database, runtime);
            var actual = await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request);
            await AnnPublicWholeFlow.PageAsync(actual, database, cut, mode);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            var empty = request with { Search = request.Search with { AllowedIds = [] } };
            actual = await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, empty);
            await AnnPublicWholeFlow.PageAsync(actual, database, cut, AnnPageMode.Exact, empty: true);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            await AnnPublicWholeFlow.PageAsync(await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request), database, cut, mode);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        });

    [Test]
    public Task ActualDisabledMissingGenerationAndResultBudgetFailWithoutProvisioningOrPartialThenHealthyPage()
        => AnnPublicWholeFlow.RunAsync(ExactThreshold, FullBreadth, async (database, runtime, pin) =>
        {
            var image = NativeAnnMaintenanceTestData.Snapshot(database);
            var cut = database.Store.Position;
            var files = NativeAnnFixtureFileSnapshot.Capture(database.Directory);
            var request = AnnPublicWholeFlow.Request(database, pin);
            await FailureAsync(() => AnnPublicWholeFlow.Engine(database, runtime, enabled: false)
                .ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request), ErrorCode.UnsupportedCapability);
            var engine = AnnPublicWholeFlow.Engine(database, runtime);
            await FailureAsync(() => engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal,
                request with { IndexGeneration = MissingGeneration }), ErrorCode.HistoryUnavailable);
            await FailureAsync(() => AnnPublicWholeFlow.Engine(database, runtime, bytes: TooSmallResult)
                .ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request), ErrorCode.BudgetExceeded);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            await Assert.That(NativeAnnFixtureFileSnapshot.Capture(database.Directory)).IsEquivalentTo(files, CollectionOrdering.Matching);
            await AnnPublicWholeFlow.PageAsync(await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request),
                database, cut, AnnPageMode.Exact);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        });

    private static async Task FailureAsync(Func<Task<AnnSearchPage>> operation, ErrorCode code)
    {
        AnnSearchPage? partial = null;
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial = await operation())
            ?? throw new InvalidOperationException("The real native ANN operation did not fail.");
        await Assert.That(failure.Code).IsEqualTo(code);
        await Assert.That(partial).IsNull();
    }
}
