using System.Text.Json;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphStorageRejectedOutcomeAssertions
{
    internal static async Task VerifyReplayAsync(TestDatabase database, CommandRequest command,
        ErrorCode expectedError, string expectedDetail, long previousPosition)
    {
        var rejected = GraphStorageReferenceFixture.Submit(database.Database, command);
        await Assert.That(rejected.Error).IsEqualTo(expectedError);
        await Assert.That(rejected.SafeDetail).IsEqualTo(expectedDetail);
        await Assert.That(rejected.Json).IsNull();
        await Assert.That(database.Store.Position).IsEqualTo(previousPosition + 1);
        var persisted = OutcomeBytes(database, command);
        var retried = GraphStorageReferenceFixture.Submit(database.Database, command);
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(retried, JsonDefaults.Options))
            .IsEquivalentTo(JsonSerializer.SerializeToUtf8Bytes(rejected, JsonDefaults.Options), CollectionOrdering.Matching);
        await Assert.That(OutcomeBytes(database, command)).IsEquivalentTo(persisted, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(previousPosition + 1);
    }

    internal static byte[] CaptureState(TestDatabase database, string vertex, string stream, string message)
        => JsonSerializer.SerializeToUtf8Bytes(new
        {
            Document = database.Database.GetDocument(GraphStorageReferenceFixture.Root,
                new(database.Partition, GraphStorageReferenceFixture.Nodes, vertex)),
            Events = CaptureStream(database, stream),
            Message = database.Database.InspectMessage(GraphStorageReferenceFixture.Root,
                new(database.Partition, GraphStorageReferenceFixture.Queue), message),
            Outbox = database.Database.GetOutboxStatus(GraphStorageReferenceFixture.Root, database.Partition)
        }, JsonDefaults.Options);

    internal static async Task VerifyStateAsync(TestDatabase database, string vertex, string stream,
        string message, byte[] expected)
    {
        await Assert.That(CaptureState(database, vertex, stream, message)).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(database.Database.ReadStream(GraphStorageReferenceFixture.Root,
            new(database.Partition, GraphStorageReferenceFixture.Events, stream)).CutPosition)
            .IsEqualTo(database.Store.Position);
    }

    private static byte[] CaptureStream(TestDatabase database, string stream)
    {
        var page = database.Database.ReadStream(GraphStorageReferenceFixture.Root,
            new(database.Partition, GraphStorageReferenceFixture.Events, stream));
        return JsonSerializer.SerializeToUtf8Bytes(new { page.Stream, page.Head, page.Events, page.HasMore }, JsonDefaults.Options);
    }

    private static byte[] OutcomeBytes(TestDatabase database, CommandRequest command)
        => database.Store.Read(view => view.ReadOwnedValue(
            OutcomeStoreOracle.PartitionKey(database.Partition, GraphStorageReferenceFixture.Root, command.CommandId)))
            ?? throw new InvalidOperationException("The rejected graph command outcome was not persisted.");
}
