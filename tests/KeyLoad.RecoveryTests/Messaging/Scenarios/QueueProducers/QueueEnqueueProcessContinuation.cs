using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueEnqueueProcessContinuation
{
    internal static async Task RunAsync(string root, DatabaseEngine owner, ZoneTreeStore store,
        Command<DatabaseCommand> original, Result<object> originalResult, CommandRequest command, CancellationToken token)
    {
        var delivery = await ClaimAckAsync(owner, store, QueueEnqueueCrashProtocol.Ready,
            QueueEnqueueCrashProtocol.Payload, QueueEnqueueCrashProtocol.First, token);
        var refusal = await QueueEnqueueProcessTerminalRefusal.RunAsync(owner, store, token);
        var message = new EnqueueMessage(QueueEnqueueCrashProtocol.Queue, QueueEnqueueCrashProtocol.Healthy,
            QueueEnqueueCrashProtocol.HealthyPayload, QueueEnqueueCrashProtocol.Headers);
        var healthy = new CommandRequest(Guid.NewGuid(), command.Partition, [message]);
        var operation = owner.OwnCommand(CrashDatabase.Operation(owner, OperationKind.Batch, healthy, healthy.CommandId));
        var target = QueueEnqueueProcessAssertions.Next(owner, store);
        var result = owner.Apply(operation);
        await QueueEnqueueProcessAssertions.ReceiptAsync(result, healthy, target);
        await QueueClaimAckRecovery.ReplayAsync(owner, store, operation, result, token);
        await ClaimAckAsync(owner, store, message.MessageId, message.PayloadJson, QueueEnqueueCrashProtocol.Second, token);
        await QueueClaimAckRecovery.ReplayAsync(owner, store, original, originalResult, token);
        await FinalAsync(owner, store, command, delivery);
        var image = QueueClaimAckRecoveryAssertions.Capture(store);
        var position = store.Position;
        var identity = store.Identity;
        store.Dispose();
        using var cold = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var restored = QueueLifecycleRecoveryAssertions.Open(cold);
        await QueueEnqueueProcessValueAssertions.IdentityAsync(cold.Identity, identity);
        await Assert.That(cold.Position).IsEqualTo(position);
        await Assert.That(QueueClaimAckRecoveryAssertions.Capture(cold)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await QueueClaimAckRecovery.ReplayAsync(restored, cold, original, originalResult, token);
        await QueueClaimAckRecovery.ReplayAsync(restored, cold, refusal.Operation, refusal.Result, token);
        await QueueClaimAckRecovery.ReplayAsync(restored, cold, operation, result, token);
        await FinalAsync(restored, cold, command, delivery);
    }

    private static async Task<Delivery> ClaimAckAsync(DatabaseEngine owner, ZoneTreeStore store,
        string id, string payload, long sequence, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), QueueEnqueueCrashProtocol.Lane, MaxMessages: QueueEnqueueCrashProtocol.First, LeaseSeconds: QueueEnqueueCrashProtocol.LeaseSeconds);
        var claim = owner.OwnCommand(CrashDatabase.Operation(owner, OperationKind.Receive, request, request.RequestId));
        var expected = QueueEnqueueProcessAssertions.Next(owner, store);
        var result = owner.Apply(claim);
        var delivery = await Assert.That(result.Get<ReceiveResult>().Deliveries).HasSingleItem();
        await QueueEnqueueProcessValueAssertions.EqualAsync(result.Get<ReceiveResult>(), new ReceiveResult(request.RequestId,
            [new(id, payload, QueueEnqueueCrashProtocol.Headers, delivery.Token, QueueEnqueueCrashProtocol.First, new DateTimeOffset(claim.Timestamp).AddSeconds(QueueEnqueueCrashProtocol.LeaseSeconds), QueueEnqueueCrashProtocol.First, QueueEnqueueCrashProtocol.First)], expected));
        await Assert.That(delivery.Token).IsNotEqualTo(string.Empty);
        await QueueEnqueueProcessValueAssertions.EqualAsync(owner.Verify<DeliveryClaims>(delivery.Token),
            new DeliveryClaims(QueueEnqueueCrashProtocol.Lane, id, CrashFixtureValues.Principal, QueueEnqueueCrashProtocol.First, QueueEnqueueCrashProtocol.First, expected.Incarnation));
        await QueueClaimAckRecovery.ReplayAsync(owner, store, claim, result, token);
        var ack = new DeliveryCommand(Guid.NewGuid(), request.Lane, delivery.Token, DeliveryAction.Ack);
        var completion = owner.OwnCommand(CrashDatabase.Operation(owner, OperationKind.Delivery, ack, ack.CommandId));
        var target = QueueEnqueueProcessAssertions.Next(owner, store);
        var receipt = owner.Apply(completion);
        await QueueEnqueueProcessValueAssertions.EqualAsync(receipt.Get<CommitReceipt>(), new CommitReceipt(ack.CommandId, target, [new(nameof(DeliveryAction.Ack), request.Lane.Queue, id, QueueEnqueueCrashProtocol.Settled)], DurabilityProfile.ProcessDurable, 1));
        await Assert.That(owner.InspectMessage(CrashFixtureValues.Principal, request.Lane, id))
            .IsEqualTo(new MessageInspection(new(id, MessageState.Acked, QueueEnqueueCrashProtocol.First, QueueEnqueueCrashProtocol.Settled, sequence, null, null, LeaseVersion: QueueEnqueueCrashProtocol.First), null, null));
        await Assert.That(store.Read(view => view.ReadOwnedValue(KeySpace.Partition(QueueEnqueueCrashProtocol.BodyFamily, request.Lane.Partition, request.Lane.Queue, id)))).IsNull();
        await QueueClaimAckRecovery.ReplayAsync(owner, store, completion, receipt, token);
        return delivery;
    }

    private static async Task FinalAsync(DatabaseEngine owner, ZoneTreeStore store, CommandRequest command, Delivery delivery)
    {
        var scheduled = command.Mutations.OfType<EnqueueMessage>().Single(message => message.MessageId == QueueEnqueueCrashProtocol.Scheduled);
        var body = new MessageBody(scheduled.MessageId, scheduled.PayloadJson, scheduled.HeadersJson, null, JsonData.Fingerprint(scheduled));
        await Assert.That(owner.InspectMessage(CrashFixtureValues.Principal, QueueEnqueueCrashProtocol.Lane, scheduled.MessageId))
            .IsEqualTo(new MessageInspection(new(scheduled.MessageId, MessageState.Scheduled, QueueEnqueueCrashProtocol.Initial, QueueEnqueueCrashProtocol.First, QueueEnqueueCrashProtocol.Initial, scheduled.NotBefore, null), body.PayloadJson, body.HeadersJson));
        await Assert.That(store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition(QueueEnqueueCrashProtocol.BodyFamily, command.Partition,
            scheduled.Queue, scheduled.MessageId)))).IsEqualTo(body);
        await Assert.That(store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(QueueEnqueueCrashProtocol.CountersFamily, command.Partition, scheduled.Queue))))
            .IsEqualTo(new QueueCounters(QueueEnqueueCrashProtocol.First, NativeSerialization.Serialize(body).LongLength, QueueEnqueueCrashProtocol.Initial, QueueEnqueueCrashProtocol.Initial, QueueEnqueueCrashProtocol.Second));
        await QueueEnqueueProcessAssertions.ReferencesAsync(store, QueueEnqueueCrashProtocol.ReadyFamily, []);
        await QueueEnqueueProcessAssertions.ReferencesAsync(store, QueueEnqueueCrashProtocol.LeaseFamily, []);
        await QueueEnqueueProcessAssertions.ReferencesAsync(store, QueueEnqueueCrashProtocol.ScheduledFamily,
            [QueueEnqueueProcessAssertions.Reference(QueueEnqueueCrashProtocol.ScheduledFamily, scheduled.NotBefore!.Value, scheduled.MessageId)]);
        foreach (var pair in new[] { (QueueEnqueueCrashProtocol.Ready, QueueEnqueueCrashProtocol.FirstLong), (QueueEnqueueCrashProtocol.Healthy, QueueEnqueueCrashProtocol.SecondLong) })
        {
            await Assert.That(owner.InspectMessage(CrashFixtureValues.Principal, QueueEnqueueCrashProtocol.Lane, pair.Item1))
                .IsEqualTo(new MessageInspection(new(pair.Item1, MessageState.Acked, QueueEnqueueCrashProtocol.First, QueueEnqueueCrashProtocol.Settled, pair.Item2, null, null, LeaseVersion: QueueEnqueueCrashProtocol.First), null, null));
            await Assert.That(store.Read(view => view.ReadOwnedValue(KeySpace.Partition(QueueEnqueueCrashProtocol.BodyFamily, command.Partition, scheduled.Queue, pair.Item1)))).IsNull();
        }
        await Assert.That(owner.GetDocument(CrashFixtureValues.Principal, new(command.Partition, QueueEnqueueCrashProtocol.Collection,
            QueueEnqueueCrashProtocol.Ready))!.Json).IsEqualTo(QueueEnqueueCrashProtocol.Payload);
        await Assert.That(delivery.Id).IsEqualTo(QueueEnqueueCrashProtocol.Ready);
    }
}
