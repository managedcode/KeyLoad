using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnLineageColdContinuation
{
    private const int MaximumReaders = 1;

    internal static async Task RunAsync(TestDatabase database, AnnMaintenanceRequest request,
        global::KeyLoad.Clustering.AnnMaintenanceCapabilityResult publication)
    {
        var source = publication.Source
            ?? throw new InvalidOperationException("The original execution lineage source cut is absent.");
        var digest = publication.IndexSha256
            ?? throw new InvalidOperationException("The original execution lineage index digest is absent.");
        var token = TestContext.Current!.Execution.CancellationToken;
        var image = Capture(database.Store);
        var identity = database.Store.Identity;
        await LoadAndSearchAsync(database.Directory, database.Store, database.Database, request,
            source, digest, token);
        await UnchangedAsync(database.Store, image);
        database.JoinOwnedResources();
        await ColdAsync(database, request, source, digest, identity, image, token);
        await ColdAsync(database, request, source, digest, identity, image, token);
    }

    private static async Task ColdAsync(TestDatabase database, AnnMaintenanceRequest request,
        AnnSourceCut source, string digest, StoreIdentity identity,
        (long Position, KeyValueRecord[] Records) image, CancellationToken token)
    {
        var failures = new List<Exception>();
        try
        {
            using var store = new ZoneTreeStore(new(database.Directory), database.StorageExecution,
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(OrleansSerialization.Serialize(store.Identity).AsSpan()
                    .SequenceEqual(OrleansSerialization.Serialize(identity))).IsTrue();
                await UnchangedAsync(store, image);
                var engine = database.CreateConfiguredDatabase(store);
                await LoadAndSearchAsync(database.Directory, store, engine, request, source, digest, token);
                await UnchangedAsync(store, image);
            }, failures);
        }
        catch (Exception cleanup) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task LoadAndSearchAsync(string directory, ZoneTreeStore store,
        DatabaseEngine database, AnnMaintenanceRequest request, AnnSourceCut source, string digest,
        CancellationToken token)
    {
        var failures = new List<Exception>();
        try
        {
            await using var owner = AnnOwnership.Create(directory, store.Identity,
                Options.Create(new AnnExecutionOptions()), UnitExecutionOptions.PackedAnn(),
                Options.Create(new PackedAnnStorageOptions()), MaximumReaders);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var seeds = new AnnSeedOptions();
                var read = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Limits),
                    database.EvaluationClock, token);
                var current = AnnSeedCollector.CapturePinned(database, AnnProjectionPinTestSupport.Principal,
                    request, Options.Create(seeds), read);
                await AnnLineageReplayOracle.AssertSeedAsync(current, AnnLineageReplayOracle.Stage.Reprojected);
                await Assert.That(current.Scope.PolicyEpoch).IsEqualTo(source.PolicyEpoch);
                await Assert.That(current.Scope.SchemaVersion).IsEqualTo(source.SchemaVersion);
                await Assert.That(current.ProjectionCheckpoint).IsEqualTo((long?)source.ThroughSequence);
                var loaded = await owner.RunAsync(stages => stages.LoadCompleted(request, current,
                    seeds, seeds.MaxPeakBytes, new AnnWorkBudget(read, seeds.MaxWorkUnits), read));
                await Assert.That(Convert.ToHexStringLower(loaded.Manifest.IndexSha256)).IsEqualTo(digest);
                await Assert.That(OrleansSerialization.Serialize(loaded.Manifest.Source).AsSpan()
                    .SequenceEqual(OrleansSerialization.Serialize(source))).IsTrue();
                await AnnLineageReplayOracle.AssertSeedAsync(loaded.Seed, AnnLineageReplayOracle.Stage.Reprojected);
                _ = owner.AdmitLoaded(loaded.Index, loaded.Manifest, current);
                using var lease = owner.Acquire(request, current);
                await AnnLineageReplayOracle.AssertSearchAsync(lease.Index,
                    new AnnWorkBudget(read, seeds.MaxWorkUnits));
            }, failures);
        }
        catch (Exception cleanup) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static (long Position, KeyValueRecord[] Records) Capture(ZoneTreeStore store)
        => store.Read(view =>
        {
            var page = view.Scan([], AnnMaintenanceTestData.SnapshotBound);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The execution lineage fixture exceeds its complete state bound.");
            }
            return (store.Position, page.Records.ToArray());
        });

    private static async Task UnchangedAsync(ZoneTreeStore store,
        (long Position, KeyValueRecord[] Records) expected)
    {
        var actual = Capture(store);
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await Assert.That(actual.Records.Length).IsEqualTo(expected.Records.Length);
        for (var index = 0; index < expected.Records.Length; index++)
        {
            await Assert.That(actual.Records[index].Key.Span.SequenceEqual(expected.Records[index].Key.Span)).IsTrue();
            await Assert.That(actual.Records[index].Value.Span.SequenceEqual(expected.Records[index].Value.Span)).IsTrue();
        }
    }
}
