using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueReadyAuthorityHealthy
{
    private const long EmptyQueueUsage = 0L;
    private const long ProcessedDocumentRevision = 1L;
    internal static async Task CompleteOriginalAsync(TestDatabase fixture, QueueLaneRef lane,
        ReceiveResult result, bool expired, DateTimeOffset now)
    {
        if (expired)
        {
            await Assert.That(result.Deliveries.IsEmpty).IsTrue();
            var inspection = fixture.Database.InspectMessage("root", lane, "input")!;
            await Assert.That(inspection.Metadata.State).IsEqualTo(MessageState.Expired);
            await Assert.That(inspection.PayloadJson).IsNull();
            await RequireEmptyQuotaAsync(fixture, lane);
        }
        else
        {
            var delivery = await Assert.That(result.Deliveries).HasSingleItem();
            await Assert.That(delivery.Id).IsEqualTo("input");
            await Assert.That(delivery.PayloadJson).IsEqualTo("{\"input\":1}");
            await ProcessAsync(fixture, lane, delivery, now);
        }
    }

    internal static async Task RunAsync(TestDatabase fixture, QueueLaneRef lane, DateTimeOffset now, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var producer = Guid.NewGuid();
        fixture.Submit(OperationKind.Batch, new CommandRequest(producer, fixture.Partition,
            [new EnqueueMessage(lane.Queue, "healthy", "{\"input\":2}")]), id: producer, time: now).Get<CommitReceipt>();
        var id = Guid.NewGuid();
        var receive = fixture.Submit(OperationKind.Receive, new ReceiveRequest(id, lane),
            principal: "worker", id: id, time: now).Get<ReceiveResult>();
        var delivery = await Assert.That(receive.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo("healthy");
        await Assert.That(delivery.PayloadJson).IsEqualTo("{\"input\":2}");
        await ProcessAsync(fixture, lane, delivery, now);
    }

    private static async Task ProcessAsync(TestDatabase fixture, QueueLaneRef lane, Delivery delivery, DateTimeOffset now)
    {
        var id = Guid.NewGuid();
        var request = new ProcessingRequest(id, lane, delivery.Token, "handler", 1,
            [new PutDocument("orders", delivery.Id, delivery.PayloadJson, 0)]);
        var result = fixture.Submit(OperationKind.Processing, request, principal: "worker", id: id, time: now).Get<CommitReceipt>();
        var original = fixture.ReadStoredOutcome(id).PreparedCommand!;
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var cut = fixture.ReadCanonicalCut();
        var replay = fixture.SubmitIssued(original).Get<CommitReceipt>();
        await Assert.That(OrleansSerialization.Serialize(replay)).IsEquivalentTo(OrleansSerialization.Serialize(result), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(image, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(fixture.ReadCanonicalCut()).IsEqualTo(cut);
        await Assert.That(fixture.Database.InspectMessage("root", lane, delivery.Id)!.Metadata.State).IsEqualTo(MessageState.Acked);
        var document = fixture.Database.GetDocument("root", new(fixture.Partition, "orders", delivery.Id))!;
        await Assert.That(document.Revision).IsEqualTo(ProcessedDocumentRevision);
        await Assert.That(document.Json).IsEqualTo(delivery.PayloadJson);
        await RequireEmptyQuotaAsync(fixture, lane);
    }

    private static async Task RequireEmptyQuotaAsync(TestDatabase fixture, QueueLaneRef lane)
    {
        var counters = fixture.Store.Read(view => view.GetRecord<QueueCounters>(
            KeySpace.Partition("queue-counters", lane.Partition, lane.Queue)))!;
        await Assert.That(counters.StoredMessages).IsEqualTo(EmptyQueueUsage);
        await Assert.That(counters.StoredBytes).IsEqualTo(EmptyQueueUsage);
        await Assert.That(counters.InFlightMessages).IsEqualTo(EmptyQueueUsage);
        await Assert.That(counters.InFlightBytes).IsEqualTo(EmptyQueueUsage);
    }
}
