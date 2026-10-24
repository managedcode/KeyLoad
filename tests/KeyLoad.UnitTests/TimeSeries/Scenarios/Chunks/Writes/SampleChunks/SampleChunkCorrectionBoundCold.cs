using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.TimeSeries;

internal static class SampleChunkCorrectionBoundCold
{
    private const int ColdOwners = 2;

    internal static async Task RunAsync(SampleChunkCanonicalFixture fixture, Guid refusedId,
        Mutation overflow, Result<object> refused, long revision, ImmutableArray<SampleRecord> rows,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals)
    {
        var refusedCommand = new CommandRequest(refusedId, fixture.Owner.Partition, [overflow]);
        var refusedOperation = fixture.Owner.Database.CreateCommand(OperationKind.Batch, refusedId,
            SampleChunkCanonicalFixture.Principal, default, JsonSerializer.Serialize(refusedCommand, JsonDefaults.Options));
        originals.Add((refusedOperation, refused));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var image = SampleChunkCorrectionBoundColdAssertions.Image(fixture.Owner.Store);
            var position = fixture.Owner.Store.Position;
            var identity = JsonDefaults.Serialize(fixture.Owner.Store.Identity);
            await WaveAsync(fixture.Owner.Database, fixture.Owner.Store, fixture, revision, rows, originals);
            fixture.Owner.Store.Dispose();
            for (var cold = 0; cold < ColdOwners; cold++)
            {
                await ColdAsync(fixture, revision, rows, originals, image, position, identity);
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ColdAsync(SampleChunkCanonicalFixture fixture, long revision,
        ImmutableArray<SampleRecord> rows, List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals,
        string image, long position, byte[] identity)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(fixture.Owner.Directory), fixture.Owner.StorageExecution,
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await SampleChunkCorrectionBoundColdAssertions.UnchangedAsync(store, image, position, identity);
                await WaveAsync(fixture.Owner.CreateConfiguredDatabase(store), store, fixture, revision, rows, originals);
            }, failures);
            await ServerFailureObserver.ObserveAsync(() =>
                SampleChunkCorrectionBoundColdAssertions.UnchangedAsync(store, image, position, identity), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WaveAsync(DatabaseEngine database, ZoneTreeStore store,
        SampleChunkCanonicalFixture fixture, long revision, ImmutableArray<SampleRecord> rows,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals)
    {
        await SampleChunkCorrectionBoundColdAssertions.RowsAsync(database, store, fixture, revision, rows);
        foreach (var original in originals)
        {
            await SampleChunkCorrectionBoundColdAssertions.ReplayAsync(database, store, original.Operation, original.Result);
        }
        await SampleChunkCorrectionBoundColdAssertions.RowsAsync(database, store, fixture, revision, rows);
    }
}
