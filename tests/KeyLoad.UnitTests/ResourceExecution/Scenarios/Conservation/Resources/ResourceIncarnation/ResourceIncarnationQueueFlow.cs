using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceIncarnationQueueFlow
{
    internal const string Queue = "identity-work";
    private const string Payload = "{\"value\":7}";

    internal static async Task RunAsync(TestDatabase fixture, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var definition = new ResourceDefinition(Queue, ResourceKind.WorkQueue, fixture.Partition.TransactionDomainId);
        var create = ResourceIncarnationLiteral.Configure(fixture.Partition, definition);
        var created = fixture.Database.Apply(create);
        await ResourceIncarnationAssertions.EqualAsync(definition, created.Get<ResourceDefinition>());
        var lifetime = ResourceIncarnationAssertions.Bytes(fixture.Store, ResourceIncarnationLiteral.LifetimeKey(fixture.Partition, Queue));
        var lane = new QueueLaneRef(fixture.Partition, Queue);
        var message = await EnqueueAsync(fixture.Database, lane, "warm", 1);
        var pause = Control(fixture.Database, lane, 1, true);
        await CorruptionAsync(fixture, pause, lifetime);
        var paused = fixture.Database.Apply(pause);
        await ResourceIncarnationAssertions.EqualAsync(definition with
        {
            SchemaVersion = 2,
            Paused = true
        }, paused.Get<ResourceDefinition>());
        await PreserveAsync(fixture.Store, lane, lifetime);
        await ResourceIncarnationAssertions.EqualAsync(message, fixture.Database.InspectMessage("root", lane, "warm")!);
        var receive = new ReceiveRequest(Guid.NewGuid(), lane);
        var blocked = Apply(fixture.Database, OperationKind.Receive, receive, receive.RequestId);
        await Assert.That(Errors.Code(blocked.Problem)).IsEqualTo(ErrorCode.DispatchPaused);
        await ResourceIncarnationAssertions.EqualAsync(message, fixture.Database.InspectMessage("root", lane, "warm")!);
        var resume = Control(fixture.Database, lane, 2, false);
        var resumed = fixture.Database.Apply(resume);
        var current = definition with
        {
            SchemaVersion = 3
        };
        await ResourceIncarnationAssertions.EqualAsync(current, resumed.Get<ResourceDefinition>());
        await PreserveAsync(fixture.Store, lane, lifetime);
        var acked = await CompleteAsync(fixture.Database, lane, "warm", 1);
        var replays = new List<(Command<DatabaseCommand> Command, Result<object> Result)> { (create, created), (pause, paused), (resume, resumed) };
        await ReplayAsync(fixture.Database, fixture.Store, replays);
        await ResourceIncarnationAssertions.EqualAsync(acked, fixture.Database.InspectMessage("root", lane, "warm")!);
        await ResourceIncarnationQueueCold.RunAsync(fixture, current, lifetime, acked, replays, token);
    }

    private static Command<DatabaseCommand> Control(DatabaseEngine database, QueueLaneRef lane, long version, bool paused)
        => new(Guid.NewGuid(), OperationKind.SetQueueDispatch, "root", database.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(new QueueDispatchControlRequest(lane.Partition.TenantId, lane.Partition.DatabaseId,
                lane.Queue, version, paused), JsonDefaults.Options));

    private static async Task CorruptionAsync(TestDatabase fixture, Command<DatabaseCommand> pause, byte[] lifetime)
    {
        var key = ResourceIncarnationLiteral.LifetimeKey(fixture.Partition, Queue);
        fixture.Store.Commit((tx, _) => { tx.Delete(key); return true; });
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.Apply(pause));
        await Assert.That(error!.Code).IsEqualTo(ErrorCode.Corruption);
        await ResourceIncarnationAssertions.UnchangedAsync(fixture.Store, image, position);
        fixture.Store.Commit((tx, _) => { tx.Put(key, lifetime); return true; });
    }

    internal static async Task<MessageInspection> EnqueueAsync(DatabaseEngine database, QueueLaneRef lane, string id, long sequence)
    {
        var command = new CommandRequest(Guid.NewGuid(), lane.Partition, [new EnqueueMessage(lane.Queue, id, Payload)]);
        var receipt = Apply(database, OperationKind.Batch, command, command.CommandId).Get<CommitReceipt>();
        await ReceiptAsync(receipt, command.CommandId, lane, new("enqueue", lane.Queue, id, 1));
        var expected = new MessageInspection(new(id, MessageState.Ready, 0, 1, sequence, null, null), Payload, "{}");
        await ResourceIncarnationAssertions.EqualAsync(expected, database.InspectMessage("root", lane, id)!);
        return expected;
    }

    internal static async Task<MessageInspection> CompleteAsync(DatabaseEngine database, QueueLaneRef lane, string id, long sequence)
    {
        var receive = new ReceiveRequest(Guid.NewGuid(), lane);
        var received = Apply(database, OperationKind.Receive, receive, receive.RequestId).Get<ReceiveResult>();
        await Assert.That(received.RequestId).IsEqualTo(receive.RequestId);
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo("{}");
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1);
        await Assert.That(delivery.Token).IsNotEmpty();
        var leased = new MessageInspection(new(id, MessageState.Leased, 1, 2, sequence, null, null,
            "root", 1, delivery.LeaseUntil), Payload, "{}");
        await ResourceIncarnationAssertions.EqualAsync(leased, database.InspectMessage("root", lane, id)!);
        var ack = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
        var receipt = Apply(database, OperationKind.Delivery, ack, ack.CommandId).Get<CommitReceipt>();
        await ReceiptAsync(receipt, ack.CommandId, lane, new("Ack", lane.Queue, id, 3));
        var expected = new MessageInspection(new(id, MessageState.Acked, 1, 3, sequence, null, null, LeaseVersion: 1), null, null);
        await ResourceIncarnationAssertions.EqualAsync(expected, database.InspectMessage("root", lane, id)!);
        return expected;
    }

    internal static async Task PreserveAsync(ZoneTreeStore store, QueueLaneRef lane, byte[] lifetime)
        => await Assert.That(ResourceIncarnationAssertions.Bytes(store,
            ResourceIncarnationLiteral.LifetimeKey(lane.Partition, lane.Queue))).IsEquivalentTo(lifetime,
                global::TUnit.Assertions.Enums.CollectionOrdering.Matching);

    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store,
        List<(Command<DatabaseCommand> Command, Result<object> Result)> originals)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        foreach (var (command, result) in originals)
        {
            await ResourceIncarnationAssertions.ResultAsync(result, database.Apply(command));
        }
        await ResourceIncarnationAssertions.UnchangedAsync(store, image, position);
    }

    private static Result<object> Apply<T>(DatabaseEngine database, OperationKind kind, T request, Guid id)
        => database.Apply(database.CreateCommand(kind, id, "root", database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(request, JsonDefaults.Options)));

    private static async Task ReceiptAsync(CommitReceipt receipt, Guid id, QueueLaneRef lane, MutationReceipt expected)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(lane.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await ResourceIncarnationAssertions.EqualAsync<System.Collections.Immutable.ImmutableArray<MutationReceipt>>([expected], receipt.Mutations);
    }
}
