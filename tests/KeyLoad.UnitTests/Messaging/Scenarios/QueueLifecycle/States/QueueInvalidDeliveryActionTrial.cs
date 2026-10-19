using KeyLoad.Core;
using KeyLoad.Storage;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueInvalidDeliveryActionTrial
{
    private const long EmptyQueueUsage = 0L;
    private const long ProcessedDocumentRevision = 1L;
    private const string Queue = "jobs";
    private const string Collection = "orders";
    private const string Worker = "worker";
    private const string Input = "input";
    private const string Output = "processed";
    private const string Detail = "Queue delivery action is invalid.";

    internal static async Task RunAsync(QueueRetryJitter jitter, int action, CancellationToken token)
    {
        var owningFailures = new List<Exception>();
        try
        {
            token.ThrowIfCancellationRequested();
            using var fixture = new TestDatabase();
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var (lane, delivery, now) = await SeedAsync(fixture, jitter);
                var before = QueueOrderedRetryImage.Capture(fixture.Store, lane);
                var id = Guid.NewGuid();
                var command = new DeliveryCommand(id, lane, delivery.Token, (DeliveryAction)action);
                var rejected = fixture.Submit(OperationKind.Delivery, command, principal: Worker, id: id, time: now);
                await RequireRefusalAsync(rejected);
                await QueueOrderedRetryImage.SameAsync(fixture.Store, lane, before);
                await RequireNoEffectsAsync(fixture, lane);
                var image = QueueWholeFlowStorage.Bytes(fixture.Store);
                var cut = fixture.ReadCanonicalCut();
                var original = fixture.ReadStoredOutcome(id).PreparedCommand
                    ?? throw new InvalidOperationException("The actual execution delivery command was not retained.");
                var replay = fixture.SubmitIssued(original);
                await RequireRefusalAsync(replay);
                await Assert.That(Convert.ToHexString(OrleansSerialization.Serialize(replay)))
                    .IsEqualTo(Convert.ToHexString(OrleansSerialization.Serialize(rejected)));
                await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(image, TUnit.Assertions.Enums.CollectionOrdering.Matching);
                await Assert.That(fixture.ReadCanonicalCut()).IsEqualTo(cut);
                token.ThrowIfCancellationRequested();
                await CompleteAsync(fixture, lane, delivery, now, before);
            }, owningFailures);
        }
        catch (Exception error) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error)) { owningFailures.Add(error); }
        catch (Exception error) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error)) { owningFailures.Add(error); }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(owningFailures);
    }

    private static async Task<(QueueLaneRef, Delivery, DateTimeOffset)> SeedAsync(TestDatabase fixture, QueueRetryJitter jitter)
    {
        fixture.Configure(Queue, ResourceKind.WorkQueue, queuePolicy: new()
        {
            RetryJitter = jitter
        });
        fixture.Configure(Collection, ResourceKind.Collection);
        fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(Worker, "tenant",
            [new("database", Queue, Capability.QueueConsume | Capability.QueueAck | Capability.QueueRenew),
                new("database", Collection, Capability.DocumentsWrite)], []))).Get<PrincipalRecord>();
        fixture.Commit(new EnqueueMessage(Queue, Input, "{\"input\":1}"));
        var now = QueueWholeFlowStorage.Clock(fixture.Store).AddSeconds(1);
        var lane = new QueueLaneRef(fixture.Partition, Queue);
        var id = Guid.NewGuid();
        var claimed = fixture.Submit(OperationKind.Receive, new ReceiveRequest(id, lane),
            principal: Worker, id: id, time: now).Get<ReceiveResult>();
        var delivery = await Assert.That(claimed.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(Input);
        await Assert.That(delivery.PayloadJson).IsEqualTo("{\"input\":1}");
        return (lane, delivery, now);
    }

    private static async Task RequireRefusalAsync(Result<object> result)
    {
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.Validation);
        await Assert.That(result.Problem?.Detail).IsEqualTo(Detail);

        await Assert.That(result.Value).IsNull();
    }

    private static async Task RequireNoEffectsAsync(TestDatabase fixture, QueueLaneRef lane)
    {
        await Assert.That(fixture.Database.GetDocument("root", new(fixture.Partition, Collection, Output))).IsNull();
        var inbox = fixture.Store.Read(view => view.Scan(KeySpace.Partition("inbox", lane.Partition, lane.Queue), 1));
        await Assert.That(inbox.Records.IsEmpty).IsTrue();
        await Assert.That(inbox.HasMore).IsFalse();
    }

    private static async Task CompleteAsync(TestDatabase fixture, QueueLaneRef lane,
        Delivery delivery, DateTimeOffset now, string[] before)
    {
        await QueueOrderedRetryImage.SameAsync(fixture.Store, lane, before);
        var renewId = Guid.NewGuid();
        fixture.Submit(OperationKind.Delivery,
            new DeliveryCommand(renewId, lane, delivery.Token, DeliveryAction.Renew, LeaseSeconds: 30),
            principal: Worker, id: renewId, time: now).Get<CommitReceipt>();
        var id = Guid.NewGuid();
        var processing = new ProcessingRequest(id, lane, delivery.Token, "handler", 1,
            [new PutDocument(Collection, Output, "{\"processed\":1}", 0)]);
        var completed = fixture.Submit(OperationKind.Processing, processing,
            principal: Worker, id: id, time: now).Get<CommitReceipt>();
        var terminal = QueueOrderedRetryImage.Capture(fixture.Store, lane);
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var cut = fixture.ReadCanonicalCut();
        var original = fixture.ReadStoredOutcome(id).PreparedCommand
            ?? throw new InvalidOperationException("The actual execution Processing command was not retained.");
        var replayed = fixture.SubmitIssued(original).Get<CommitReceipt>();
        await Assert.That(Convert.ToHexString(OrleansSerialization.Serialize(replayed)))
            .IsEqualTo(Convert.ToHexString(OrleansSerialization.Serialize(completed)));
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(image, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(fixture.ReadCanonicalCut()).IsEqualTo(cut);
        var next = Guid.NewGuid();
        var sameHandler = fixture.Submit(OperationKind.Processing, processing with
        {
            CommandId = next
        },
            principal: Worker, id: next, time: now).Get<CommitReceipt>();
        await Assert.That(Convert.ToHexString(OrleansSerialization.Serialize(sameHandler)))
            .IsEqualTo(Convert.ToHexString(OrleansSerialization.Serialize(completed)));
        await QueueOrderedRetryImage.SameAsync(fixture.Store, lane, terminal);
        await Assert.That(fixture.Database.InspectMessage("root", lane, Input)!.Metadata.State).IsEqualTo(MessageState.Acked);
        await Assert.That(fixture.Database.InspectMessage("root", lane, Input)!.PayloadJson).IsNull();
        var counters = fixture.Store.Read(view => view.GetRecord<QueueCounters>(
            KeySpace.Partition("queue-counters", lane.Partition, lane.Queue)))!;
        await Assert.That(counters.StoredMessages).IsEqualTo(EmptyQueueUsage);
        await Assert.That(counters.StoredBytes).IsEqualTo(EmptyQueueUsage);
        await Assert.That(counters.InFlightMessages).IsEqualTo(EmptyQueueUsage);
        await Assert.That(counters.InFlightBytes).IsEqualTo(EmptyQueueUsage);
        var document = fixture.Database.GetDocument("root", new(fixture.Partition, Collection, Output))!;
        await Assert.That(document.Revision).IsEqualTo(ProcessedDocumentRevision);
        await Assert.That(document.Json).IsEqualTo("{\"processed\":1}");
    }
}
