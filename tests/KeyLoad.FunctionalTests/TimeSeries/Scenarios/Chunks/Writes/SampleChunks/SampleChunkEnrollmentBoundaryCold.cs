using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class SampleChunkEnrollmentBoundaryCold
{
    private const int ColdOwners = 2;

    internal static async Task RunAsync(SampleChunkCanonicalFixture fixture, Guid window,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals)
    {
        var image = SampleChunkCorrectionBoundColdAssertions.Image(fixture.Owner.Store);
        var position = fixture.Owner.Store.Position;
        var identity = JsonDefaults.Serialize(fixture.Owner.Store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture.Owner.Database,
            fixture.Owner.Store, fixture, window, originals), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture.Owner.Store.Dispose();
            for (var wave = 0; wave < ColdOwners; wave++)
            {
                await ServerFailureObserver.ObserveAsync(() => ColdAsync(
                    fixture, window, originals, image, position, identity), failures);
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ColdAsync(SampleChunkCanonicalFixture fixture, Guid window,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals, string image, long position, byte[] identity)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(fixture.Owner.Directory), fixture.Owner.StorageExecution,
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.UnchangedAsync(
                store, image, position, identity), failures);
            await ServerFailureObserver.ObserveAsync(() => WaveAsync(
                fixture.Owner.CreateConfiguredDatabase(store), store, fixture, window, originals), failures);
            await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.UnchangedAsync(
                store, image, position, identity), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WaveAsync(DatabaseEngine database, ZoneTreeStore store, SampleChunkCanonicalFixture fixture,
        Guid window, List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => SampleChunkEnrollmentBoundaryRows.CurrentAsync(
            database, store, fixture, window, populated: true, sealedWindow: true), failures);
        foreach (var original in originals)
        {
            await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.ReplayAsync(
                database, store, original.Operation, original.Result), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => SampleChunkEnrollmentBoundaryRows.CurrentAsync(
            database, store, fixture, window, populated: true, sealedWindow: true), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
