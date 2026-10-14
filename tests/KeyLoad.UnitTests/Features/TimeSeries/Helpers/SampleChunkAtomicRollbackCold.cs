using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkAtomicRollbackCold
{
    private const int ColdOwners = 2;

    internal static async Task RunAsync(SampleChunkCanonicalFixture fixture,
        List<(ReplicatedOperation Operation, OperationResult Result)> originals)
    {
        var image = SampleChunkCorrectionBoundColdAssertions.Image(fixture.Owner.Store);
        var position = fixture.Owner.Store.Position;
        var identity = JsonDefaults.Serialize(fixture.Owner.Store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => WaveAsync(
            fixture.Owner.Database, fixture.Owner.Store, fixture, originals), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture.Owner.Store.Dispose();
            for (var cold = 0; cold < ColdOwners; cold++)
            {
                await ServerFailureObserver.ObserveAsync(() => ColdAsync(
                    fixture, originals, image, position, identity), failures);
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ColdAsync(SampleChunkCanonicalFixture fixture,
        List<(ReplicatedOperation Operation, OperationResult Result)> originals, string image, long position, byte[] identity)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(fixture.Owner.Directory), fixture.Owner.StorageExecution,
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.UnchangedAsync(
                store, image, position, identity), failures);
            await ServerFailureObserver.ObserveAsync(() => WaveAsync(
                fixture.Owner.CreateConfiguredDatabase(store), store, fixture, originals), failures);
            await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.UnchangedAsync(
                store, image, position, identity), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WaveAsync(DatabaseEngine database, ZoneTreeStore store, SampleChunkCanonicalFixture fixture,
        List<(ReplicatedOperation Operation, OperationResult Result)> originals)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RowsAsync(database, store, fixture, healthy: true), failures);
        foreach (var original in originals)
        {
            await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.ReplayAsync(
                database, store, original.Operation, original.Result), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => RowsAsync(database, store, fixture, healthy: true), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task RowsAsync(DatabaseEngine database, ZoneTreeStore store,
        SampleChunkCanonicalFixture fixture, bool healthy)
    {
        var image = SampleChunkCorrectionBoundColdAssertions.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var rows = SampleChunkCanonicalFixture.Expected(healthy);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var expected = new SampleChunkWindowResult(fixture.WindowId, SampleChunkCanonicalFixture.Start,
                SampleChunkCanonicalFixture.Until,
                healthy ? SampleChunkCanonicalFixture.MergedGeneration : SampleChunkCanonicalFixture.NoGeneration,
                healthy ? SampleChunkCanonicalFixture.MergedRevision : SampleChunkCanonicalFixture.AppendedRevision,
                healthy ? SampleChunkCanonicalFixture.CorrectedSequence : SampleChunkCanonicalFixture.InitialSequence,
                null, [.. rows], position);
            var actual = database.ReadSampleChunkWindow(SampleChunkCanonicalFixture.Principal, fixture.Request);
            await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var raw = database.ReadSamples(SampleChunkCanonicalFixture.Principal, fixture.Owner.Partition,
                SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, SampleChunkCanonicalFixture.Start,
                SampleChunkCanonicalFixture.Until, SampleChunkCanonicalFixture.OutputLimit);
            await Assert.That(JsonDefaults.Serialize(raw).SequenceEqual(JsonDefaults.Serialize(rows))).IsTrue();
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.UnchangedAsync(
            store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
