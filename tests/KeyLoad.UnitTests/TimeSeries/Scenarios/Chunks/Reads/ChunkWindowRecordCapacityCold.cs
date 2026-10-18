using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class ChunkWindowRecordCapacityCold
{
    private const int ColdOwners = 2;

    internal static async Task RunAsync(SampleChunkCanonicalFixture fixture, SampleChunkRetentionClock clock,
        Guid freshWindow, List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals)
    {
        var image = SampleChunkCorrectionBoundColdAssertions.Image(fixture.Owner.Store);
        var position = fixture.Owner.Store.Position;
        var identity = JsonDefaults.Serialize(fixture.Owner.Store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture.Owner.Database,
            fixture.Owner.Store, fixture, freshWindow, originals), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture.Owner.Store.Dispose();
            for (var cold = 0; cold < ColdOwners; cold++)
            {
                await ServerFailureObserver.ObserveAsync(() => ColdAsync(
                    fixture, clock, freshWindow, originals, image, position, identity), failures);
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ColdAsync(SampleChunkCanonicalFixture fixture, SampleChunkRetentionClock clock,
        Guid freshWindow, List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals,
        string image, long position, byte[] identity)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(fixture.Owner.Directory), fixture.Owner.StorageExecution,
                UnitExecutionOptions.PointCacheExecution(), timeProvider: clock);
            await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.UnchangedAsync(
                store, image, position, identity), failures);
            await ServerFailureObserver.ObserveAsync(() => WaveAsync(
                fixture.Owner.CreateConfiguredDatabase(store), store, fixture, freshWindow, originals), failures);
            await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.UnchangedAsync(
                store, image, position, identity), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WaveAsync(DatabaseEngine database, ZoneTreeStore store, SampleChunkCanonicalFixture fixture,
        Guid freshWindow, List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => ChunkWindowRecordCapacityAssertions.HealthyAsync(
            database, store, fixture, freshWindow), failures);
        await ServerFailureObserver.ObserveAsync(() => ChunkWindowRecordCapacityAssertions.RetiredAsync(
            database, store, fixture), failures);
        await ServerFailureObserver.ObserveAsync(() => ChunkWindowRecordCapacityAssertions.RetentionAsync(
            database, store, fixture, ChunkMultiblockBoundaryData.InitialCount, more: false), failures);
        await ServerFailureObserver.ObserveAsync(() => ChunkWindowRecordCapacityAssertions.PhysicalAsync(
            store, fixture, ChunkMultiblockBoundaryData.InitialCount, fresh: true), failures);
        await ServerFailureObserver.ObserveAsync(() => ReplayAsync(database, store, originals), failures);
        await ServerFailureObserver.ObserveAsync(() => ChunkWindowRecordCapacityAssertions.HealthyAsync(
            database, store, fixture, freshWindow), failures);
        await ServerFailureObserver.ObserveAsync(() => ChunkWindowRecordCapacityAssertions.RetiredAsync(
            database, store, fixture), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals)
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
