using ManagedCode.Communication.Commands;
using System.Text;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryAdapterCollectionFixture
{
    internal const string Collection = "orders";
    internal const string First = "{\"values\":[2,4],\"fixed\":[2,4],\"slots\":[2,4],\"status\":\"open\"}";
    private const string Second = "{\"values\":[8,16],\"fixed\":[8,16],\"slots\":[8,16],\"status\":\"closed\"}";
    private static readonly Guid SeedId = new("208dd62e-1dcf-4f3e-b929-c3c7ee217d51");

    internal static async Task<List<(Command<DatabaseCommand> Operation, byte[] Result)>> SeedAsync(TestDatabase fixture)
    {
        fixture.Configure(Collection, ResourceKind.Collection);
        var first = Encoding.UTF8.GetString(JsonDefaults.Serialize(new QueryAdapterCollectionDocument([2m, 4m],
            [2m, 4m], [2m, 4m], "open")));
        var second = Encoding.UTF8.GetString(JsonDefaults.Serialize(new QueryAdapterCollectionDocument([8m, 16m],
            [8m, 16m], [8m, 16m], "closed")));
        await Assert.That(first).IsEqualTo(First);
        await Assert.That(second).IsEqualTo(Second);
        var originals = new List<(Command<DatabaseCommand> Operation, byte[] Result)>();
        var before = fixture.Store.Position;
        var identity = fixture.Store.Identity;
        var command = new CommandRequest(SeedId, fixture.Partition,
            [new PutDocument(Collection, "a", first), new PutDocument(Collection, "b", second)]);
        var actual = QueryAdapterNullParityReplay.Apply(fixture.Database, OperationKind.Batch,
            SeedId, command, originals).Get<CommitReceipt>();
        var expected = new CommitReceipt(SeedId, new(identity.Incarnation, fixture.Partition.AtomicPartitionId, before + 1), [new("putDocument", Collection, "a", 1), new("putDocument", Collection, "b", 1)], identity.Durability, (identity.Durability == DurabilityProfile.Buffered ? 0 : 1));
        await Assert.That(JsonDefaults.Serialize(actual)).IsEquivalentTo(JsonDefaults.Serialize(expected), CollectionOrdering.Matching);
        return originals;
    }
}
