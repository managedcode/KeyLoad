using System.Text.Json;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineSignedRetry
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var fixture = new TestDatabase();
        fixture.Configure(RuntimeQueueDeadlineProtocol.Queue, ResourceKind.WorkQueue, queuePolicy: new()
        {
            RetryJitter = QueueRetryJitter.Full,
            RetryBaseMilliseconds = RuntimeQueueDeadlineProtocol.RetryCapMilliseconds
        });
        var lane = new QueueLaneRef(fixture.Partition, RuntimeQueueDeadlineProtocol.Queue);
        var start = QueueWholeFlowStorage.Clock(fixture.Store);
        fixture.Commit(new EnqueueMessage(lane.Queue, RuntimeQueueDeadlineProtocol.Message, RuntimeQueueDeadlineProtocol.Payload));
        start = QueueWholeFlowStorage.Clock(fixture.Store);
        QueueDeadlineOperations.ConfigureWorker(fixture.Database, lane.Partition, start,
            Capability.QueueConsume | Capability.QueueAck | Capability.QueueInspect, RuntimeQueueDeadlineProtocol.First);
        var receive = new ReceiveRequest(Guid.NewGuid(), lane);
        var delivery = QueueDeadlineOperations.Apply(fixture.Database, OperationKind.Receive, receive,
            receive.RequestId, start, RuntimeQueueDeadlineProtocol.Worker).Get<ReceiveResult>().Deliveries.Single();
        var request = new CommandRequest(Guid.NewGuid(), lane.Partition, [new AdvanceQueueDeadline(lane.Queue,
            delivery.Id, MessageState.Leased, RuntimeQueueDeadlineProtocol.Second, delivery.LeaseVersion,
            delivery.LeaseUntil, QueueDeadlineKind.ExpireLease)]);
        var operation = fixture.Database.PrepareQueueRetryOperation(fixture.Database.OwnCommand(fixture.Database.CreateCommand(OperationKind.Batch, request.CommandId, RuntimeQueueDeadlineProtocol.Worker, delivery.LeaseUntil, JsonSerializer.Serialize(request, JsonDefaults.Options))));
        var wrapper = operation.Value!;
        var choices = OrleansSerialization.Deserialize<QueueRetryDecisions>(wrapper.RetryDecisions.Span);
        var choice = choices.Items.Single();
        await Assert.That(choice.DueIndexKey.ToArray()).IsEquivalentTo(KeySpace.Partition(
            RuntimeQueueDeadlineProtocol.LeaseSpace, lane.Partition, lane.Queue, delivery.LeaseUntil, delivery.Id), CollectionOrdering.Matching);
        await Assert.That(choice.DelayMilliseconds).IsGreaterThanOrEqualTo((int)RuntimeQueueDeadlineProtocol.First);
        await Assert.That(choice.DelayMilliseconds).IsLessThanOrEqualTo(RuntimeQueueDeadlineProtocol.RetryCapMilliseconds);
        await Assert.That(choice.RetryAt).IsEqualTo(new DateTimeOffset(operation.Timestamp).AddMilliseconds(choice.DelayMilliseconds));
        await QueueDeadlineAssertions.AlteredRetryDrawAsync(fixture.Database, fixture.Store, lane, operation);
        var result = fixture.Database.Apply(operation);
        result.Get<CommitReceipt>();
        var expected = new MessageMetadata(delivery.Id, MessageState.Scheduled, delivery.Attempt,
            RuntimeQueueDeadlineProtocol.Third, RuntimeQueueDeadlineProtocol.First, choice.RetryAt, null,
            LeaseVersion: delivery.LeaseVersion, SafeFailureCode: RuntimeQueueDeadlineProtocol.RetryCode);
        await QueueDeadlineAssertions.OriginalAsync(fixture.Database, lane, expected);
        var bytes = QueueRetryColdAssertions.LaneBytes(fixture.Store, lane);
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(cold);
        await ReplayResultAssertions.Same<CommitReceipt>(database.Apply(operation), result);
        await Assert.That(QueueRetryColdAssertions.LaneBytes(cold, lane)).IsEquivalentTo(bytes, CollectionOrdering.Matching);
        await QueueDeadlineSignedRetryContinuation.RunAsync(database, lane, expected, delivery, choice.RetryAt);
    }
}
