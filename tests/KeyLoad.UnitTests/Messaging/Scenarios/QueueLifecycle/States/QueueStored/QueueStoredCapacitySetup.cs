using System.Text.Json;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueStoredCapacitySetup
{
    internal static async Task<QueueStoredCapacityState> SeedAsync(TestDatabase fixture, CancellationToken token)
    {
        fixture.Configure(QueueEnqueueColdProtocol.Collection, ResourceKind.Collection);
        fixture.Configure(QueueEnqueueColdProtocol.Queue, ResourceKind.WorkQueue, queuePolicy: new()
        {
            MaxStoredMessages = QueueEnqueueColdProtocol.MaximumStoredMessages
        });
        var scheduledAt = TimeProvider.System.GetUtcNow().AddHours(QueueEnqueueColdProtocol.FutureScheduledHours);
        var producer = new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new PutDocument(QueueEnqueueColdProtocol.Collection, QueueEnqueueColdProtocol.Document, QueueEnqueueColdProtocol.ReadyJson),
                new EnqueueMessage(QueueEnqueueColdProtocol.Queue, QueueEnqueueColdProtocol.Ready,
                    QueueEnqueueColdProtocol.ReadyJson, QueueEnqueueColdProtocol.Headers),
                new EnqueueMessage(QueueEnqueueColdProtocol.Queue, QueueEnqueueColdProtocol.Scheduled,
                    QueueEnqueueColdProtocol.ScheduledJson, QueueEnqueueColdProtocol.Headers, NotBefore: scheduledAt)]);
        var expected = await QueueStoredCapacityReceipts.NextAsync(fixture.Database, fixture.Store, fixture.Partition);
        var original = QueueEnqueueColdTrial.Apply(fixture.Database, producer, QueueEnqueueColdProtocol.Root, token);
        await QueueStoredCapacityReceipts.ReceiptAsync(original, producer.CommandId, expected,
            [new(QueueEnqueueColdProtocol.PutKind, QueueEnqueueColdProtocol.Collection, QueueEnqueueColdProtocol.Document, QueueEnqueueAuthorityProtocol.First),
                new(QueueEnqueueColdProtocol.EnqueueKind, QueueEnqueueColdProtocol.Queue, QueueEnqueueColdProtocol.Ready, QueueEnqueueAuthorityProtocol.First),
                new(QueueEnqueueColdProtocol.EnqueueKind, QueueEnqueueColdProtocol.Queue, QueueEnqueueColdProtocol.Scheduled, QueueEnqueueAuthorityProtocol.First)]);
        var time = QueueWholeFlowStorage.Clock(fixture.Store);
        var lane = new QueueLaneRef(fixture.Partition, QueueEnqueueColdProtocol.Queue);
        var enqueue = new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new PutDocument(QueueEnqueueColdProtocol.Collection, QueueEnqueueColdProtocol.RefusedDocument, QueueEnqueueColdProtocol.HealthyJson),
                new EnqueueMessage(QueueEnqueueColdProtocol.Queue, QueueEnqueueColdProtocol.Healthy, QueueEnqueueColdProtocol.HealthyJson)]);
        var receive = new ReceiveRequest(Guid.NewGuid(), lane);
        return new(producer, original, scheduledAt,
            Operation(OperationKind.Batch, enqueue, enqueue.CommandId, time),
            Operation(OperationKind.Receive, receive, receive.RequestId, time));
    }

    internal static Command<DatabaseCommand> Operation<T>(OperationKind kind, T payload, Guid id, DateTimeOffset time)
        => new(id, kind, QueueEnqueueColdProtocol.Root, time, JsonSerializer.Serialize(payload, JsonDefaults.Options));
}
