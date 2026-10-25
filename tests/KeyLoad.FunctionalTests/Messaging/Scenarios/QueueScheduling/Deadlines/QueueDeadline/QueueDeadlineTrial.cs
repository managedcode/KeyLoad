using KeyLoad.FunctionalTests.ResourceExecution;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueDeadlineTrial
{
    internal static async Task CorruptBodyAsync(QueueDeadlineKind kind, bool receiveSweep, CancellationToken token)
    {
        using var fixture = new TestDatabase();
        fixture.Configure(RuntimeQueueDeadlineProtocol.Queue, ResourceKind.WorkQueue);
        var lane = new QueueLaneRef(fixture.Partition, RuntimeQueueDeadlineProtocol.Queue);
        var start = QueueWholeFlowStorage.Clock(fixture.Store);
        var due = start.AddSeconds(30);
        var producer = new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new EnqueueMessage(lane.Queue, "input", "{\"work\":1}", "{\"kind\":\"job\"}", NotBefore: due)]);
        var produced = QueueDeadlineOperations.Batch(fixture.Database, producer, start, "root");
        produced.Get<CommitReceipt>();
        var metadata = new MessageMetadata("input", MessageState.Scheduled, 0, 1, 0, due, null);
        if (kind == QueueDeadlineKind.ExpireLease)
        {
            var request = new ReceiveRequest(Guid.NewGuid(), lane);
            var original = QueueDeadlineOperations.Apply(fixture.Database, OperationKind.Receive,
                request, request.RequestId, due).Get<ReceiveResult>().Deliveries.Single();
            await Assert.That(original.PayloadJson).IsEqualTo("{\"work\":1}");
            await Assert.That(original.HeadersJson).IsEqualTo("{\"kind\":\"job\"}");
            metadata = new("input", MessageState.Leased, 1, 3, 1, null, null, "root", 1, due.AddSeconds(30));
            due = due.AddSeconds(30);
        }
        await Assert.That(fixture.Database.InspectMessage("root", lane, "input")).IsEqualTo(
            new MessageInspection(metadata, "{\"work\":1}", "{\"kind\":\"job\"}"));
        var operation = DueBodyOperation(fixture.Database, lane, metadata, due, kind, receiveSweep);
        var originalBytes = QueueWholeFlowStorage.Bytes(fixture.Store);
        var key = KeySpace.Partition("message-body", lane.Partition, lane.Queue, "input");
        var originalBody = fixture.Store.Commit((tx, _) =>
        {
            var bytes = tx.ReadOwnedValue(key) ?? throw new InvalidOperationException("Original body is absent.");
            tx.PutRecord(key, OrleansSerialization.Deserialize<MessageBody>(bytes) with
            {
                Id = "foreign"
            });
            return bytes;
        });
        var damaged = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        await RefuseDueBodyAsync(fixture.Database, fixture.Store, operation, damaged, position, identity);
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        using var first = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(first);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(first, damaged, position, identity);
        await RefuseDueBodyAsync(database, first, operation, damaged, position, identity);
        first.Commit((tx, _) => { tx.Put(key, originalBody); return true; });
        await Assert.That(QueueWholeFlowStorage.Bytes(first)).IsEquivalentTo(originalBytes,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await CompleteDueBodyAsync(database, first, fixture.Directory, lane, operation, producer,
            produced, due, kind, receiveSweep, identity, token);
    }

    private static Command<DatabaseCommand> DueBodyOperation(DatabaseEngine database, QueueLaneRef lane,
        MessageMetadata metadata, DateTimeOffset due, QueueDeadlineKind kind, bool receiveSweep)
    {
        var id = Guid.NewGuid();
        var deadline = new AdvanceQueueDeadline(lane.Queue, "input", metadata.State, metadata.StateVersion,
            metadata.LeaseVersion, due, kind);
        var json = receiveSweep
            ? System.Text.Json.JsonSerializer.Serialize(new ReceiveRequest(id, lane), JsonDefaults.Options)
            : System.Text.Json.JsonSerializer.Serialize(new CommandRequest(id, lane.Partition, [deadline]), JsonDefaults.Options);
        return database.PrepareQueueRetryOperation(database.OwnCommand(
            database.CreateCommand(receiveSweep ? OperationKind.Receive : OperationKind.Batch, id, "root", due, json)));
    }

    private static async Task RefuseDueBodyAsync(DatabaseEngine database, ZoneTreeStore store,
        Command<DatabaseCommand> operation, string[] image, long position, byte[] identity)
    {
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.Apply(operation));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(failure.Message).IsEqualTo("A queue message body is absent.");
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
        }
    }

    private static async Task CompleteDueBodyAsync(DatabaseEngine database, ZoneTreeStore store, string directory,
        QueueLaneRef lane, Command<DatabaseCommand> operation, CommandRequest producer, Result<object> produced,
        DateTimeOffset due, QueueDeadlineKind kind, bool receiveSweep, byte[] identity, CancellationToken token)
    {
        var result = database.Apply(operation);
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        Delivery delivery;
        if (receiveSweep)
        {
            await ReplayResultAssertions.Same<ReceiveResult>(database.Apply(operation), result);
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, bytes, position, identity);
            delivery = await Assert.That(result.Get<ReceiveResult>().Deliveries).HasSingleItem();
        }
        else
        {
            await ReplayResultAssertions.Same<CommitReceipt>(database.Apply(operation), result);
            var ready = new MessageMetadata("input", MessageState.Ready,
                kind == QueueDeadlineKind.ExpireLease ? 1 : 0, kind == QueueDeadlineKind.ExpireLease ? 4 : 2,
                kind == QueueDeadlineKind.ExpireLease ? 2 : 1, null, null,
                LeaseVersion: kind == QueueDeadlineKind.ExpireLease ? 1 : 0);
            await Assert.That(database.InspectMessage("root", lane, "input")).IsEqualTo(
                new MessageInspection(ready, "{\"work\":1}", "{\"kind\":\"job\"}"));
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, bytes, position, identity);
            var request = new ReceiveRequest(Guid.NewGuid(), lane);
            delivery = await Assert.That(QueueDeadlineOperations.Apply(database, OperationKind.Receive,
                request, request.RequestId, due).Get<ReceiveResult>().Deliveries).HasSingleItem();
        }
        await Assert.That(delivery.Id).IsEqualTo("input");
        await Assert.That(delivery.PayloadJson).IsEqualTo("{\"work\":1}");
        await Assert.That(delivery.HeadersJson).IsEqualTo("{\"kind\":\"job\"}");
        await Assert.That(delivery.Attempt).IsEqualTo(kind == QueueDeadlineKind.ExpireLease ? 2 : 1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(kind == QueueDeadlineKind.ExpireLease ? 2L : 1L);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        await Assert.That(delivery.LeaseUntil).IsEqualTo(due.AddSeconds(30));
        var ack = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
        var acked = QueueDeadlineOperations.Apply(database, OperationKind.Delivery, ack, ack.CommandId, due);
        acked.Get<CommitReceipt>();
        var terminal = new MessageInspection(new("input", MessageState.Acked,
            kind == QueueDeadlineKind.ExpireLease ? 2 : 1, kind == QueueDeadlineKind.ExpireLease ? 6 : 4,
            kind == QueueDeadlineKind.ExpireLease ? 2 : 1, null, null,
            LeaseVersion: kind == QueueDeadlineKind.ExpireLease ? 2 : 1), null, null);
        await Assert.That(database.InspectMessage("root", lane, "input")).IsEqualTo(terminal);
        await QueueDeadlineColdHealthyContinuation.RunAsync(store, directory, lane, ack, acked, producer, produced, terminal,
            due, identity, token);
    }

    internal static async Task RunAsync(QueueDeadlineKind kind, CancellationToken token)
    {
        using var fixture = new TestDatabase();
        fixture.Configure(RuntimeQueueDeadlineProtocol.Queue, ResourceKind.WorkQueue);
        var start = QueueWholeFlowStorage.Clock(fixture.Store);
        var due = start.AddSeconds(RuntimeQueueDeadlineProtocol.DueSeconds);
        var lane = new QueueLaneRef(fixture.Partition, RuntimeQueueDeadlineProtocol.Queue);
        QueueDeadlineOperations.ConfigureWorker(fixture.Database, fixture.Partition, start,
            Capability.QueueConsume | Capability.QueueInspect | Capability.QueueAck, RuntimeQueueDeadlineProtocol.First);
        var expiry = kind == QueueDeadlineKind.ExpireMessage ? due : (DateTimeOffset?)null;
        var notBefore = kind == QueueDeadlineKind.ExpireMessage ? null : (DateTimeOffset?)due;
        var seed = new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new EnqueueMessage(lane.Queue, RuntimeQueueDeadlineProtocol.Message, RuntimeQueueDeadlineProtocol.Payload,
                RuntimeQueueDeadlineProtocol.Headers, NotBefore: notBefore, ExpiresAt: expiry)]);
        var seeded = QueueDeadlineOperations.Batch(fixture.Database, seed, start, RuntimeQueueDeadlineProtocol.Root);
        seeded.Get<CommitReceipt>();
        var metadata = new MessageMetadata(RuntimeQueueDeadlineProtocol.Message,
            notBefore is null ? MessageState.Ready : MessageState.Scheduled, (int)RuntimeQueueDeadlineProtocol.Initial,
            RuntimeQueueDeadlineProtocol.First, notBefore is null ? RuntimeQueueDeadlineProtocol.First : RuntimeQueueDeadlineProtocol.Initial,
            notBefore, expiry);
        await QueueDeadlineAssertions.OriginalAsync(fixture.Database, lane, metadata);
        if (kind == QueueDeadlineKind.ExpireLease)
        {
            (metadata, due) = await QueueDeadlineLease.CreateAsync(fixture.Database, lane, metadata, due);
        }
        var advance = new AdvanceQueueDeadline(lane.Queue, metadata.Id, metadata.State, metadata.StateVersion,
            metadata.LeaseVersion, due, kind);
        var command = new CommandRequest(Guid.NewGuid(), lane.Partition, [advance]);
        await QueueDeadlineAssertions.RefusedAsync(fixture.Database, fixture.Store, lane, command,
            due.AddTicks(RuntimeQueueDeadlineProtocol.PreviousTick), ErrorCode.Conflict);
        command = command with
        {
            CommandId = Guid.NewGuid()
        };
        var stale = command with
        {
            CommandId = Guid.NewGuid(),
            Mutations = [advance with { ExpectedStateVersion = metadata.StateVersion + RuntimeQueueDeadlineProtocol.First }]
        };
        await QueueDeadlineAssertions.RefusedAsync(fixture.Database, fixture.Store, lane, stale, due, ErrorCode.RevisionConflict);
        await RuntimeQueueDeadlineDiscovery.RequireAsync(fixture, lane, advance, due, token);
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        using var first = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(first);
        await QueueDeadlineAssertions.OriginalAsync(database, lane, metadata);
        await QueueDeadlineContinuation.RunAsync(database, first, fixture.Directory, lane, command, seeded,
            seed, metadata, due, token);
    }
}
