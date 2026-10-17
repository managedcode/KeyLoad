using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkCorrectionTagCold
{
    private const int ColdOwners = 2;

    internal static async Task RunAsync(SampleChunkCanonicalFixture fixture,
        List<(ReplicatedOperation Operation, OperationResult Result)> originals)
    {
        var image = SampleChunkCorrectionBoundColdAssertions.Image(fixture.Owner.Store);
        var position = fixture.Owner.Store.Position;
        var identity = JsonDefaults.Serialize(fixture.Owner.Store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture.Owner.Database, fixture.Owner.Store, fixture, originals), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture.Owner.Store.Dispose();
            for (var wave = 0; wave < ColdOwners; wave++)
            {
                await ServerFailureObserver.ObserveAsync(() => ColdAsync(fixture, originals, image, position, identity), failures);
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
        await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionTagRows.RunAsync(database, store, fixture, merged: true), failures);
        await ServerFailureObserver.ObserveAsync(() => ReplayAsync(database, store, originals), failures);
        await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionTagRows.RunAsync(database, store, fixture, merged: true), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store,
        List<(ReplicatedOperation Operation, OperationResult Result)> originals)
    {
        var failures = new List<Exception>();
        foreach (var original in originals)
        {
            await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.ReplayAsync(
                database, store, original.Operation, original.Result), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
