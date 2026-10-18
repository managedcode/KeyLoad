using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkPendingBoundCold
{
    private const int ColdOwners = 2;

    internal static async Task RunAsync(SampleChunkCanonicalFixture fixture, SampleChunkPendingBoundTrial trial,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var image = SampleChunkCorrectionBoundColdAssertions.Image(fixture.Owner.Store);
            var position = fixture.Owner.Store.Position;
            var identity = JsonDefaults.Serialize(fixture.Owner.Store.Identity);
            await ServerFailureObserver.ObserveAsync(() => WaveAsync(
                fixture.Owner.Database, fixture.Owner.Store, trial, originals), failures);
            fixture.Owner.Store.Dispose();
            for (var cold = 0; cold < ColdOwners; cold++)
            {
                await ServerFailureObserver.ObserveAsync(() => ColdAsync(
                    fixture, trial, originals, image, position, identity), failures);
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ColdAsync(SampleChunkCanonicalFixture fixture, SampleChunkPendingBoundTrial trial,
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
                fixture.Owner.CreateConfiguredDatabase(store), store, trial, originals), failures);
            await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.UnchangedAsync(
                store, image, position, identity), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WaveAsync(DatabaseEngine database, ZoneTreeStore store, SampleChunkPendingBoundTrial trial,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => trial.RequireAsync(database, store,
            firstMerged: true, secondCorrected: true), failures);
        foreach (var original in originals)
        {
            await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.ReplayAsync(
                database, store, original.Operation, original.Result), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => trial.RequireAsync(database, store,
            firstMerged: true, secondCorrected: true), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
