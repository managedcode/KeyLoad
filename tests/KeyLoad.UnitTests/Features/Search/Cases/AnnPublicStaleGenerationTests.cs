using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnPublicStaleGenerationTests
{
    private const int FullCorpus = 3;
    private const long Revision = 1;
    private const int Version = 1;
    private const string Json = "{}";
    private const string UpdatedId = "seed-00000";

    [Test]
    public Task CanonicalVectorChangeRejectsStalePageThenExplicitNativeRestoreReturnsCompleteLiteralPage()
        => AnnPublicWholeFlow.RunAsync(FullCorpus, FullCorpus, async (database, runtime, pin) =>
        {
            AnnSeedTestSupport.CommitVector(database, UpdatedId, [2f, 0f, 0f]);
            var image = NativeAnnMaintenanceTestData.Snapshot(database);
            var cut = database.Store.Position;
            var files = NativeAnnFixtureFileSnapshot.Capture(database.Directory);
            var request = AnnPublicWholeFlow.Request(database, pin);
            var engine = AnnPublicWholeFlow.Engine(database, runtime);
            AnnSearchPage? partial = null;
            var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
                partial = await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request))
                ?? throw new InvalidOperationException("The stale native ANN generation did not fail.");
            await Assert.That(error.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
            await Assert.That(partial).IsNull();
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            await Assert.That(NativeAnnFixtureFileSnapshot.Capture(database.Directory))
                .IsEquivalentTo(files, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await runtime.Owner.AbortAsync(runtime.SessionId);
            var restore = pin with { CommandId = Guid.NewGuid(), Mode = AnnMaintenanceMode.Restore };
            var began = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, restore,
                AnnMaintenanceCapabilityKind.Begin);
            _ = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, restore,
                AnnMaintenanceCapabilityKind.Load);
            _ = await NativeAnnMaintenancePhaseAssertions.FinishAsync(database, runtime, restore,
                began.Source!.ThroughSequence);
            image = NativeAnnMaintenanceTestData.Snapshot(database);
            cut = database.Store.Position;
            var page = await engine.ApproximateSearchAsync(AnnProjectionPinTestSupport.Principal, request);
            var expected = new AnnSearchPage(Version,
                [Row(database, UpdatedId, 1d / 61d), Row(database, "seed-00002", 1d / 62d),
                 Row(database, "seed-00001", 1d / 63d)], cut, AnnPageMode.Exact, true,
                pin.IndexGeneration, AnnPageMode.Approximate);
            await Assert.That(JsonDefaults.Serialize(page).AsSpan()
                .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        });

    private static RankedDocument Row(TestDatabase database, string id, double score)
        => new(new(new(database.Partition, AnnSeedTestSupport.Collection, id), Revision, Json, false, []), score);
}
