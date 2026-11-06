using System.Text;
using KeyLoad.FunctionalTests.DatabaseComposition;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.StorageReplication;

internal sealed class ReplicaDeliveryPayloadTests
{
    private const string Queue = "replica-payload-queue";
    private const string Message = "original-message";
    private const string Payload = "{\"work\":\"one-retained-delivery-payload-73f486fe030f4e6fa8299bbc62e92761\"}";

    [Test]
    public Task CopiesContainOneOriginalDeliveryPayloadAndReplayReturnsItsExactLeaseWithoutPublishing()
        => DatabaseCompositionReplicaCut.RunAsync(Seed, async (source, replica) =>
        {
            var id = Guid.NewGuid();
            var lane = new QueueLaneRef(source.Partition, Queue);
            var original = source.Database.CreateCommand(OperationKind.Receive, id, GroupCommitCommands.Root,
                source.Database.EvaluationClock.GetUtcNow(), new ReceiveRequest(id, lane));
            var decided = await PublishAsync(source, original);
            var delivery = await Assert.That(decided.Applied.Single().Result.Get<ReceiveResult>().Deliveries).HasSingleItem();
            await Assert.That(delivery.PayloadJson).IsEqualTo(Payload);
            var effects = decided.Replication ?? throw new InvalidOperationException("The primary did not retain its delivery effects.");
            var encoded = OrleansSerialization.Serialize(effects);
            await Assert.That(Occurrences(encoded, Encoding.UTF8.GetBytes(Payload))).IsEqualTo(1);
            var copied = await replica.Database.ApplyReplicaBatchAsync(effects, GroupCommitCommands.Token);
            await Assert.That(copied.Outcome).IsNull();
            await Assert.That(copied.Replication).IsNull();
            await Assert.That(copied.Applied.Single()).IsEqualTo(new(effects.Position, true, null));
            await SameImageAsync(source, replica);
            var position = replica.Store.Position;
            var prepared = replica.Database.PrepareStorageOperations([original], GroupCommitCommands.Token);
            var replayed = await replica.Database.ApplyStorageOperationsAsync(prepared, effects.Position + 1, GroupCommitCommands.Token);
            await Assert.That(OrleansSerialization.Serialize(replayed.Applied.Single().Result.Get<ReceiveResult>()))
                .IsEquivalentTo(OrleansSerialization.Serialize(decided.Applied.Single().Result.Get<ReceiveResult>()));
            await Assert.That(replica.Store.Position).IsEqualTo(position);
            await SameImageAsync(source, replica);
            id = Guid.NewGuid();
            var acknowledged = await PublishAsync(source, source.Database.CreateCommand(OperationKind.Delivery,
                id, GroupCommitCommands.Root, source.Database.EvaluationClock.GetUtcNow(),
                new DeliveryCommand(id, lane, delivery.Token, DeliveryAction.Ack)));
            await replica.Database.ApplyReplicaBatchAsync(acknowledged.Replication!, GroupCommitCommands.Token);
            await SameImageAsync(source, replica);
            await Assert.That(replica.Database.InspectMessage(GroupCommitCommands.Root, lane, Message)!.Metadata.State)
                .IsEqualTo(MessageState.Acked);
        });

    private static TestDatabase Seed()
    {
        var source = new TestDatabase();
        source.Configure(Queue, ResourceKind.WorkQueue);
        source.Commit(new EnqueueMessage(Queue, Message, Payload));
        return source;
    }

    private static Task<StorageBatchResult> PublishAsync(TestDatabase source, Command<DatabaseCommand> operation)
    {
        var prepared = source.Database.PrepareStorageOperations([operation], GroupCommitCommands.Token);
        var before = NativeStoreAssertions.Applied(source.Store, source.Partition);
        return source.Database.ApplyStorageOperationsAsync(prepared, before + 1, GroupCommitCommands.Token, retainReplication: true);
    }

    private static async Task SameImageAsync(TestDatabase source, DatabaseCompositionReplicaCut replica)
        => await Assert.That(DatabaseCompositionReplicaCut.ReplicatedImage(replica.Store))
            .IsEquivalentTo(DatabaseCompositionReplicaCut.ReplicatedImage(source.Store));

    private static int Occurrences(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> payload)
    {
        var count = 0;
        while (bytes.IndexOf(payload) is var offset && offset >= 0)
        {
            count++;
            bytes = bytes[(offset + payload.Length)..];
        }
        return count;
    }
}
