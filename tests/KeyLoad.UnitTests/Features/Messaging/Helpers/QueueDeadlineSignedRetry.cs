using KeyLoad.UnitTests.Features.ResourceExecution;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineSignedRetry
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var fixture = new TestDatabase();
        fixture.Configure(QueueDeadlineNativeProtocol.Queue, ResourceKind.WorkQueue, queuePolicy: new()
        { RetryJitter = QueueRetryJitter.Full, RetryBaseMilliseconds = QueueDeadlineNativeProtocol.RetryCapMilliseconds });
        var lane = new QueueLaneRef(fixture.Partition, QueueDeadlineNativeProtocol.Queue);
        var start = QueueWholeFlowStorage.Clock(fixture.Store);
        fixture.Commit(new EnqueueMessage(lane.Queue, QueueDeadlineNativeProtocol.Message, QueueDeadlineNativeProtocol.Payload));
        start = QueueWholeFlowStorage.Clock(fixture.Store);
        QueueDeadlineNativeOperations.ConfigureWorker(fixture.Database, lane.Partition, start,
            Capability.QueueConsume | Capability.QueueAck | Capability.QueueInspect, QueueDeadlineNativeProtocol.First);
        var receive = new ReceiveRequest(Guid.NewGuid(), lane);
        var delivery = QueueDeadlineNativeOperations.Apply(fixture.Database, OperationKind.Receive, receive,
            receive.RequestId, start, QueueDeadlineNativeProtocol.Worker).Get<ReceiveResult>().Deliveries.Single();
        var request = new CommandRequest(Guid.NewGuid(), lane.Partition, [new AdvanceQueueDeadline(lane.Queue,
            delivery.Id, MessageState.Leased, QueueDeadlineNativeProtocol.Second, delivery.LeaseVersion,
            delivery.LeaseUntil, QueueDeadlineKind.ExpireLease)]);
        var operation = fixture.Database.PrepareQueueRetryOperation(fixture.Database.NormalizeOperation(new(request.CommandId,
            OperationKind.Batch, QueueDeadlineNativeProtocol.Worker, delivery.LeaseUntil,
            JsonSerializer.Serialize(request, JsonDefaults.Options))));
        var wrapper = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
        var choices = NativeSerialization.Deserialize<QueueRetryDecisions>(wrapper.RetryDecisions.Span);
        var choice = choices.Items.Single();
        await Assert.That(choice.DueIndexKey.ToArray()).IsEquivalentTo(KeySpace.Partition(
            QueueDeadlineNativeProtocol.LeaseSpace, lane.Partition, lane.Queue, delivery.LeaseUntil, delivery.Id), CollectionOrdering.Matching);
        await Assert.That(choice.DelayMilliseconds).IsGreaterThanOrEqualTo((int)QueueDeadlineNativeProtocol.First);
        await Assert.That(choice.DelayMilliseconds).IsLessThanOrEqualTo(QueueDeadlineNativeProtocol.RetryCapMilliseconds);
        await Assert.That(choice.RetryAt).IsEqualTo(operation.EvaluatedAt.AddMilliseconds(choice.DelayMilliseconds));
        var result = fixture.Database.Apply(operation);
        result.Get<CommitReceipt>();
        var expected = new MessageMetadata(delivery.Id, MessageState.Scheduled, delivery.Attempt,
            QueueDeadlineNativeProtocol.Third, QueueDeadlineNativeProtocol.First, choice.RetryAt, null,
            LeaseVersion: delivery.LeaseVersion, SafeFailureCode: QueueDeadlineNativeProtocol.RetryCode);
        await QueueDeadlineNativeAssertions.OriginalAsync(fixture.Database, lane, expected);
        var bytes = QueueRetryColdAssertions.LaneBytes(fixture.Store, lane);
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(cold);
        await NativeReplayResultAssertions.Same<CommitReceipt>(database.Apply(operation), result);
        await Assert.That(QueueRetryColdAssertions.LaneBytes(cold, lane)).IsEquivalentTo(bytes, CollectionOrdering.Matching);
        await QueueDeadlineSignedRetryContinuation.RunAsync(database, lane, expected, delivery, choice.RetryAt);
    }
}
