using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnPublicWholeFlowTests
{
    private const int ExactThreshold = 3;
    private const int ApproximateThreshold = 0;
    private const int FullBreadth = 3;
    private const int NarrowBreadth = 1;
    private const int TooSmallResult = 1;
    private const long MissingGeneration = 2;

    [Test]
    [Arguments(ExactThreshold, FullBreadth, AnnPageMode.Exact)]
    [Arguments(ApproximateThreshold, FullBreadth, AnnPageMode.Approximate)]
    [Arguments(ApproximateThreshold, NarrowBreadth, AnnPageMode.Approximate)]
    public Task ActualProvisionedSearchReportsModeOrBoundedRefusalThenHealthyAndEmptyPages(int threshold, int breadth, AnnPageMode mode)
        => AnnPublicWholeFlow.RunAsync(threshold, breadth, async (database, runtime, pin) =>
        {
            var image = AnnMaintenanceTestData.Snapshot(database);
            var cut = database.Store.Position;
            var request = AnnPublicWholeFlow.Request(database, pin);
            var engine = AnnPublicWholeFlow.Engine(database, runtime);
            if (threshold == ApproximateThreshold && breadth == NarrowBreadth)
            {
                await BoundedThenHealthyAsync(database, runtime, pin, request);
                return;
            }
            var actual = await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request);
            await AnnPublicWholeFlow.PageAsync(actual, database, cut, mode);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            var empty = request with
            {
                Search = request.Search with
                {
                    AllowedIds = []
                }
            };
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
            var image = AnnMaintenanceTestData.Snapshot(database);
            var cut = database.Store.Position;
            var files = AnnFixtureFileSnapshot.Capture(database.Directory);
            var request = AnnPublicWholeFlow.Request(database, pin);
            await FailureAsync(() => AnnPublicWholeFlow.Engine(database, runtime, enabled: false)
                .ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request), ErrorCode.UnsupportedCapability);
            var engine = AnnPublicWholeFlow.Engine(database, runtime);
            await FailureAsync(() => engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal,
                request with
                {
                    IndexGeneration = MissingGeneration
                }), ErrorCode.HistoryUnavailable);
            await FailureAsync(() => AnnPublicWholeFlow.Engine(database, runtime, bytes: TooSmallResult)
                .ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request), ErrorCode.BudgetExceeded);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            await Assert.That(AnnFixtureFileSnapshot.Capture(database.Directory)).IsEquivalentTo(files, CollectionOrdering.Matching);
            await AnnPublicWholeFlow.PageAsync(await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request),
                database, cut, AnnPageMode.Exact);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        });

    [Test]
    [Arguments(FullBreadth, AnnPageMode.Approximate)]
    [Arguments(NarrowBreadth, AnnPageMode.Approximate)]
    public Task ActualFilteredSearchDeduplicatesIdsOrRefusesInsufficientBreadthThenRemainsHealthy(int breadth, AnnPageMode mode)
        => AnnPublicWholeFlow.RunAsync(ApproximateThreshold, breadth, async (database, runtime, pin) =>
        {
            var image = AnnMaintenanceTestData.Snapshot(database);
            var cut = database.Store.Position;
            var files = AnnFixtureFileSnapshot.Capture(database.Directory);
            var original = AnnPublicWholeFlow.Request(database, pin);
            var request = original with
            {
                Search = original.Search with
                {
                    AllowedIds = ["seed-00000", "missing-ann-id", "seed-00000"]
                }
            };
            if (breadth == NarrowBreadth)
            {
                await BoundedThenHealthyAsync(database, runtime, pin, request);
                return;
            }
            var expected = new AnnSearchPage(AnnPublicWholeFlow.Version,
                [new RankedDocument(new DocumentResult(
                    database.ExpectedReference(new(database.Partition, AnnSeedTestSupport.Collection, "seed-00000")),
                    1, "{}", false, []), 1d / 61d)], cut, mode,
                mode != AnnPageMode.Approximate, pin.IndexGeneration, AnnPageMode.Approximate);
            var engine = AnnPublicWholeFlow.Engine(database, runtime);
            var token = TestContext.Current!.Execution.CancellationToken;
            var actual = await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request, token);
            await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            var repeated = await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request, token);
            await Assert.That(JsonDefaults.Serialize(repeated).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            var healthy = await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, original, token);
            await AnnPublicWholeFlow.PageAsync(healthy, database, cut, mode);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            await Assert.That(AnnFixtureFileSnapshot.Capture(database.Directory))
                .IsEquivalentTo(files, CollectionOrdering.Matching);
        });

    private static async Task BoundedThenHealthyAsync(TestDatabase database, AnnMaintenanceTestRuntime runtime,
        AnnMaintenanceRequest pin, ApproximateSearchRequest request)
    {
        var image = AnnMaintenanceTestData.Snapshot(database);
        var cut = database.Store.Position;
        var engine = AnnPublicWholeFlow.Engine(database, runtime);
        await FailureAsync(() => engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request), ErrorCode.BudgetExceeded);
        await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        var healthy = request with { Search = request.Search with { Limit = 1, AllowedIds = null } };
        var actual = await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, healthy);
        var expected = new AnnSearchPage(AnnPublicWholeFlow.Version,
            [new(new(database.ExpectedReference(new(database.Partition, AnnSeedTestSupport.Collection, "seed-00002")), 1, "{}", false, []), 1d / 61d)],
            cut, AnnPageMode.Approximate, false, pin.IndexGeneration, AnnPageMode.Approximate);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        var empty = healthy with { Search = healthy.Search with { AllowedIds = [] } };
        await AnnPublicWholeFlow.PageAsync(await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, empty),
            database, cut, AnnPageMode.Exact, empty: true);
        await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
    }

    private static async Task FailureAsync(Func<Task<AnnSearchPage>> operation, ErrorCode code)
    {
        AnnSearchPage? partial = null;
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial = await operation())
            ?? throw new InvalidOperationException("The real execution ANN operation did not fail.");
        await Assert.That(failure.Code).IsEqualTo(code);
        await Assert.That(partial).IsNull();
    }
}
