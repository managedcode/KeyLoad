using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryAdapterBranchFlow
{
    private const string Principal = "root";
    private const string Collection = "orders";
    private const int ColdWaves = 2;
    private const int FirstWave = 0;
    private static readonly Guid SeedId = new("6f4b7b40-8d34-41dd-ae6e-f902d9c97615");

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                fixture.Configure(Collection, ResourceKind.Collection);
                var command = new CommandRequest(SeedId, fixture.Partition,
                    [new PutDocument(Collection, "a", "{\"number\":2,\"status\":\"open\"}"),
                    new PutDocument(Collection, "b", "{\"number\":4,\"status\":\"closed\"}"),
                    new PutDocument(Collection, "c", "{\"number\":1,\"status\":\"hold\"}")]);
                var operation = new ReplicatedOperation(command.CommandId, OperationKind.Batch, Principal,
                    default, System.Text.Json.JsonSerializer.Serialize(command, JsonDefaults.Options));
                var receipt = fixture.Database.ApplyEmbedded(operation, cancellationToken: default).Get<CommitReceipt>();
                await Assert.That(receipt.CommandId).IsEqualTo(SeedId);
                await WaveAsync(fixture.Database, fixture.Store, fixture.Partition, operation, receipt);
                var image = SqlInnerJoinRetainedIdentityFlow.Image(fixture.Store);
                var position = fixture.Store.Position;
                var identity = JsonDefaults.Serialize(fixture.Store.Identity);
                fixture.Store.Dispose();
                for (var wave = FirstWave; wave < ColdWaves; wave++)
                {
                    await ColdAsync(fixture, operation, receipt, image, position, identity);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ColdAsync(TestDatabase fixture, ReplicatedOperation operation, CommitReceipt receipt,
        string[] image, long position, byte[] identity)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution,
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await SqlOffsetRefusals.UnchangedAsync(store, image, position, identity);
                await WaveAsync(fixture.CreateConfiguredDatabase(store), store, fixture.Partition, operation, receipt);
                await SqlOffsetRefusals.UnchangedAsync(store, image, position, identity);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WaveAsync(DatabaseEngine database, ZoneTreeStore store, PartitionRef partition,
        ReplicatedOperation operation, CommitReceipt receipt)
    {
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        await QueryAdapterBranchPages.RunAsync(engine, store, partition);
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var replay = database.ApplyEmbedded(operation, cancellationToken: default).Get<CommitReceipt>();
            await Assert.That(JsonDefaults.Serialize(replay)).IsEquivalentTo(JsonDefaults.Serialize(receipt),
                CollectionOrdering.Matching);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => SqlOffsetRefusals.UnchangedAsync(store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
