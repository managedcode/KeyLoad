using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class EventMessageSensitiveSafeReplayTrial
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var fixture = new TestDatabase();
        var policy = new SensitiveFieldPolicy(EventMessageSensitiveReplayProtocol.MissingPath,
            EventMessageSensitiveReplayProtocol.Classification, EventMessageSensitiveReplayProtocol.GrantBefore);
        var resource = fixture.Configure(EventMessageSensitiveReplayProtocol.Resource, ResourceKind.WorkQueue, fields: [policy]);
        var worker = new PrincipalRecord(EventMessageSensitiveReplayProtocol.Worker, fixture.Partition.TenantId,
            [new(fixture.Partition.DatabaseId, resource.Name, Capability.QueueConsume | Capability.QueueAck | Capability.QueueInspect)],
            [EventMessageSensitiveReplayProtocol.GrantBefore]);
        EventMessageSensitiveReplayOperations.Principal(fixture.Database, worker, token);
        var state = new EventMessageSensitiveReplayState(fixture.Partition, resource, worker, false, false, false);
        fixture.Commit(new EnqueueMessage(resource.Name, EventMessageSensitiveReplayProtocol.First, EventMessageSensitiveReplayProtocol.SafePayload));
        state.OriginalId = Guid.NewGuid();
        state.OriginalRequest = EventMessageSensitiveReplayOperations.Receive(state, state.OriginalId, false);
        state.Original = EventMessageSensitiveReplayOperations.Apply(fixture.Database, OperationKind.Receive, state.OriginalRequest, state.Caller, state.OriginalId, token);
        state.OriginalOutcome = EventMessageSensitiveReplayOperations.Outcome(fixture.Store, state);
        state.Resource = EventMessageSensitiveReplayOperations.ReplacePolicy(fixture.Database, state, false, token);
        await SameAsync(fixture.Database, fixture.Store, state, token);
        var identity = fixture.Store.Identity;
        fixture.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(reopened);
        await Assert.That(reopened.Identity.NodeId).IsEqualTo(identity.NodeId);
        await Assert.That(reopened.Identity.Incarnation).IsEqualTo(identity.Incarnation);
        await SameAsync(database, reopened, state, token);
        await ContinueAsync(database, state, token);
    }

    private static async Task SameAsync(DatabaseEngine database, ZoneTreeStore store,
        EventMessageSensitiveReplayState state, CancellationToken token)
    {
        var replay = EventMessageSensitiveReplayOperations.Apply(database, OperationKind.Receive,
            state.OriginalRequest, state.Caller, state.OriginalId, token);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(state.Original))).IsTrue();
        await Assert.That(EventMessageSensitiveReplayOperations.Outcome(store, state).SequenceEqual(state.OriginalOutcome)).IsTrue();
        await ValueAsync(replay.Get<ReceiveResult>());
    }

    private static async Task ContinueAsync(DatabaseEngine database, EventMessageSensitiveReplayState state, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new EnqueueMessage(state.Resource.Name, EventMessageSensitiveReplayProtocol.Second, EventMessageSensitiveReplayProtocol.SafePayload)]);
        await EventMessageSensitiveReplayAssertions.DeniedAsync(EventMessageSensitiveReplayOperations.Apply(database,
            OperationKind.Batch, command, state.Caller, command.CommandId, token), ErrorCode.PermissionDenied);
        await Assert.That(database.InspectMessage(EventMessageSensitiveReplayProtocol.Root, state.Lane, EventMessageSensitiveReplayProtocol.Second)).IsNull();
        var producer = command with { CommandId = Guid.NewGuid() };
        var produced = EventMessageSensitiveReplayOperations.Apply(database, OperationKind.Batch, producer,
            EventMessageSensitiveReplayProtocol.Root, producer.CommandId, token);
        produced.Get<CommitReceipt>();
        var ack = Guid.NewGuid();
        EventMessageSensitiveReplayOperations.Apply(database, OperationKind.Delivery,
            new DeliveryCommand(ack, state.Lane, state.Original.Get<ReceiveResult>().Deliveries.Single().Token, DeliveryAction.Ack), state.Caller, ack, token).Get<CommitReceipt>();
        var claim = Guid.NewGuid();
        var fresh = EventMessageSensitiveReplayOperations.Apply(database, OperationKind.Receive,
            EventMessageSensitiveReplayOperations.Receive(state, claim, true), state.Caller, claim, token).Get<ReceiveResult>();
        await ValueAsync(fresh);
        await Assert.That(fresh.Deliveries.Single().Id).IsEqualTo(EventMessageSensitiveReplayProtocol.Second);
        var replay = EventMessageSensitiveReplayOperations.Apply(database, OperationKind.Batch, producer,
            EventMessageSensitiveReplayProtocol.Root, producer.CommandId, token);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(produced))).IsTrue();
        var finalAck = Guid.NewGuid();
        EventMessageSensitiveReplayOperations.Apply(database, OperationKind.Delivery,
            new DeliveryCommand(finalAck, state.Lane, fresh.Deliveries.Single().Token, DeliveryAction.Ack), state.Caller, finalAck, token).Get<CommitReceipt>();
        var emptyId = Guid.NewGuid();
        var empty = EventMessageSensitiveReplayOperations.Apply(database, OperationKind.Receive,
            EventMessageSensitiveReplayOperations.Receive(state, emptyId, true), state.Caller, emptyId, token).Get<ReceiveResult>();
        await Assert.That(empty.Deliveries).IsEmpty();
    }

    private static async Task ValueAsync(ReceiveResult received)
    {
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        using var expected = JsonDocument.Parse(EventMessageSensitiveReplayProtocol.SafePayload);
        using var actual = JsonDocument.Parse(delivery.PayloadJson);
        await Assert.That(JsonElement.DeepEquals(actual.RootElement, expected.RootElement)).IsTrue();
        await Assert.That(delivery.HeadersJson).IsEqualTo(EventMessageSensitiveReplayProtocol.EmptyHeaders);
    }
}
