namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnPublicPersistedCallerTests
{
    private const int Threshold = 3;
    private const int Breadth = 3;
    private const long PublicPolicy = 2;
    private const long ChangedPolicy = 3;
    private const long MissingGeneration = 2;
    private const Capability PublicRead = Capability.Query | Capability.DocumentsRead | Capability.VectorSearch;
    private const string Denied = "ann-public-no-grant";
    private const string RivalOwner = "ann-public-rival-owner";
    private const string DeniedDetail = "The principal cannot perform this operation in this scope.";

    [Test]
    public Task ActualOrdinaryPersistedCallerReadsRedactedPageWhileNoGrantPrecedesGenerationAndRowsStayHidden()
        => AnnPublicWholeFlow.RunAsync(Threshold, Breadth, async (database, runtime, pin) =>
        {
            AnnSeedTestSupport.Persist(database, Denied, Capability.None, []);
            AnnSeedTestSupport.Persist(database, AnnSeedTestSupport.Principal, PublicRead,
                [AnnSeedTestSupport.FieldUse], owner: AnnSeedTestSupport.Owner, restrictRows: true, policyEpoch: PublicPolicy);
            var image = NativeAnnMaintenanceTestData.Snapshot(database);
            var cut = database.Store.Position;
            var request = AnnPublicWholeFlow.Request(database, pin);
            var engine = AnnPublicWholeFlow.Engine(database, runtime);
            AnnSearchPage? partial = null;
            var rejection = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial =
                await engine.ApproximateSearchAsync(Denied, request with { IndexGeneration = MissingGeneration }))
                ?? throw new InvalidOperationException("The actual no-grant caller was not rejected before generation lookup.");
            await Assert.That(rejection.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(rejection.Message).IsEqualTo(DeniedDetail);
            await Assert.That(partial).IsNull();
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            var actual = await engine.ApproximateSearchAsync(AnnSeedTestSupport.Principal, request);
            await AnnPublicWholeFlow.PageAsync(actual, database, cut, AnnPageMode.Exact, redacted: true);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            AnnSeedTestSupport.Persist(database, AnnSeedTestSupport.Principal, PublicRead,
                [AnnSeedTestSupport.FieldUse], owner: RivalOwner, restrictRows: true, policyEpoch: ChangedPolicy);
            image = NativeAnnMaintenanceTestData.Snapshot(database);
            cut = database.Store.Position;
            actual = await engine.ApproximateSearchAsync(AnnSeedTestSupport.Principal, request);
            await AnnPublicWholeFlow.PageAsync(actual, database, cut, AnnPageMode.Exact, empty: true);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            await AnnPublicWholeFlow.PageAsync(await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request),
                database, cut, AnnPageMode.Exact);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        });
}
