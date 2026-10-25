using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueryAdapterNullParityFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                fixture.Configure(QueryAdapterNullParityProtocol.Collection, ResourceKind.Collection);
                var originals = new List<(Command<DatabaseCommand> Operation, byte[] Result)>();
                var expectedToken = fixture.NextCommitToken();
                var identity = fixture.Store.Identity;
                var command = new CommandRequest(QueryAdapterNullParityProtocol.SeedId, fixture.Partition,
                    [.. QueryAdapterNullParityProtocol.Seed]);
                var receipt = QueryAdapterNullParityReplay.Apply(fixture.Database, OperationKind.Batch,
                    command.CommandId, command, originals).Get<CommitReceipt>();
                var expected = new CommitReceipt(command.CommandId, expectedToken, [new("putDocument", "orders", "null", 1), new("putDocument", "orders", "missing", 1),
                     new("putDocument", "orders", "value", 1)], identity.Durability, (identity.Durability == DurabilityProfile.Buffered ? 0 : 1));
                await Assert.That(JsonDefaults.Serialize(receipt)).IsEquivalentTo(JsonDefaults.Serialize(expected), CollectionOrdering.Matching);
                await WaveAsync(fixture.Database, fixture.Store, fixture.Partition, 1, originals);
                var retained = (SqlInnerJoinRetainedIdentityFlow.Image(fixture.Store), fixture.Store.Position,
                    JsonDefaults.Serialize(fixture.Store.Identity));
                fixture.Store.Dispose();
                for (var cold = 0; cold < 2; cold++)
                {
                    retained = await QueryAdapterNullParityCold.RunAsync(fixture, 3 + cold * 2, originals, retained);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task WaveAsync(DatabaseEngine database, ZoneTreeStore store, PartitionRef partition,
        int epoch, List<(Command<DatabaseCommand> Operation, byte[] Result)> originals)
    {
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await QueryAdapterNullParityPages.HealthyAsync(engine, store, partition, QueryAdapterNullParityProtocol.Root);
            await QueryAdapterNullParityPages.RefusalsAsync(engine, store, partition);
            await QueryAdapterNullParityPolicy.RequireAsync(database, store, partition, epoch, originals);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => QueryAdapterNullParityReplay.RequireAsync(database, store, originals), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
