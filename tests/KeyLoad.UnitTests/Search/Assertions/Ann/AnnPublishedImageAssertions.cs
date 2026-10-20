using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using Microsoft.Extensions.Options;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnPublishedImageAssertions
{
    private const int Count = 3;
    private const int Readers = 1;
    private const long Revision = 1;
    private static readonly string[] Ids = ["seed-00000", "seed-00001", "seed-00002"];
    private static readonly float[][] Values = [[0.25f, 1f, -0.5f], [1.25f, 1f, -0.5f], [2.25f, 1f, -0.5f]];

    internal static async Task AssertAsync(TestDatabase database, AnnMaintenanceRequest request, string digest)
    {
        var failures = new List<Exception>();
        var before = AnnMaintenanceTestData.Snapshot(database);
        var position = database.Store.Position;
        try
        {
            await using var owner = AnnOwnership.Create(database.Directory, database.Store.Identity,
                Options.Create(new AnnExecutionOptions()), UnitExecutionOptions.PackedAnn(),
                Options.Create(new PackedAnnStorageOptions()), Readers);
            var actual = owner;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var seeds = new AnnSeedOptions();
                var read = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
                var current = AnnSeedCollector.CapturePinned(database.Database, AnnProjectionPinTestSupport.Principal,
                    request, Options.Create(seeds), read);
                var work = new AnnWorkBudget(read, seeds.MaxWorkUnits);
                var loaded = await actual.RunAsync(stages => stages.LoadCompleted(request, current, seeds,
                    seeds.MaxPeakBytes, work, read));
                await Assert.That(Convert.ToHexStringLower(loaded.Manifest.IndexSha256)).IsEqualTo(digest);
                await Assert.That(loaded.Seed.Records.Length).IsEqualTo(Count);
                for (var index = 0; index < Count; index++)
                {
                    var row = loaded.Seed.Records[index];
                    await Assert.That(row.DocumentId).IsEqualTo(Ids[index]);
                    await Assert.That(row.Field).IsEqualTo(AnnSeedTestSupport.Field);
                    await Assert.That(row.Space).IsEqualTo(AnnSeedTestSupport.Space());
                    await Assert.That(row.DocumentRevision).IsEqualTo(Revision);
                    await Assert.That(row.Values).IsEquivalentTo(Values[index], CollectionOrdering.Matching);
                }
                _ = actual.AdmitLoaded(loaded.Index, loaded.Manifest, current);
                await AnnPinnedLifetimeAssertions.AssertAsync(database, actual, request, current);
                await Assert.That(AnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(before, CollectionOrdering.Matching);
                await Assert.That(database.Store.Position).IsEqualTo(position);
            }, failures);
        }
        catch (Exception cleanup) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
