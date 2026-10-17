using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueAdmissionCancellationWholeFlowTests
{
    [Test]
    [Arguments(OperationKind.Receive)]
    [Arguments(OperationKind.Processing)]
    public async Task AcMsg001And002CancelledAdmissionHasNoPersistedEffectsAndSameIdIsHealthy(OperationKind kind)
    {
        using var fixture = new TestDatabase();
        fixture.Configure("jobs", ResourceKind.WorkQueue);
        fixture.Configure("orders", ResourceKind.Collection);
        fixture.Configure("events", ResourceKind.StreamSet);
        fixture.Commit(new EnqueueMessage("jobs", "input", "{\"work\":1}"));
        var lane = new QueueLaneRef(fixture.Partition, "jobs");
        var id = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new ReceiveRequest(id, lane), JsonDefaults.Options);
        if (kind == OperationKind.Processing)
        {
            var receiveId = Guid.NewGuid();
            var delivery = fixture.Submit(OperationKind.Receive, new ReceiveRequest(receiveId, lane), id: receiveId)
                .Get<ReceiveResult>().Deliveries.Single();
            payload = JsonSerializer.Serialize(new ProcessingRequest(id, lane, delivery.Token, "worker", 1,
                [new PutDocument("orders", "processed", "{\"done\":true}", 0),
                    new AppendEvents("events", "processed", [new("done", "Completed", "{\"done\":true}")], ExpectedStreamRevision.NoStream),
                    new EnqueueMessage("jobs", "output", "{\"next\":true}")]), JsonDefaults.Options);
        }
        var before = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        var coordinator = new EmbeddedCoordinator(fixture.Database);
        OperationResult? partial = null;
        var failure = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            partial = await coordinator.SubmitAsync(kind, id, "root", payload, canceled.Token));
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.CancellationToken).IsEqualTo(canceled.Token);
        await Assert.That(partial).IsNull();
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(before, CollectionOrdering.Matching);
        var healthy = await coordinator.SubmitAsync(kind, id, "root", payload);
        await AssertHealthyAsync(fixture, lane, kind, healthy);
        var after = QueueWholeFlowStorage.Bytes(fixture.Store);
        var retry = await coordinator.SubmitAsync(kind, id, "root", payload);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(after, CollectionOrdering.Matching);
        if (kind == OperationKind.Receive)
        {
            await NativeReplayResultAssertions.Same<ReceiveResult>(retry, healthy);
        }
        else
        {
            await NativeReplayResultAssertions.Same<CommitReceipt>(retry, healthy);
        }
    }

    private static async Task AssertHealthyAsync(TestDatabase fixture, QueueLaneRef lane,
        OperationKind kind, OperationResult healthy)
    {
        if (kind == OperationKind.Receive)
        {
            var delivery = await Assert.That(healthy.Get<ReceiveResult>().Deliveries).HasSingleItem();
            await Assert.That(delivery.Id).IsEqualTo("input");
            await Assert.That(delivery.PayloadJson).IsEqualTo("{\"work\":1}");
            await Assert.That(delivery.Attempt).IsEqualTo(1);
        }
        else
        {
            healthy.Get<CommitReceipt>();
            await Assert.That(fixture.Database.InspectMessage("root", lane, "input")!.Metadata.State).IsEqualTo(MessageState.Acked);
            var document = fixture.Database.GetDocument("root", new(fixture.Partition, "orders", "processed"))!;
            await Assert.That(document.Reference).IsEqualTo(new EntityRef(fixture.Partition, "orders", "processed"));
            await Assert.That(document.Json).IsEqualTo("{\"done\":true}");
            await Assert.That(document.Revision).IsEqualTo(1L);
            await Assert.That(document.Redacted).IsFalse();
            await Assert.That(document.RedactedFields).IsEmpty();
            var page = fixture.Database.ReadStream("root", new StreamRef(fixture.Partition, "events", "processed"));
            await Assert.That(page.Head).IsEqualTo(new StreamHead(1, 1, 1));
            await Assert.That(page.HasMore).IsFalse();
            var expected = new EventRecord(new(fixture.Partition, "events", "processed"), 1, 1,
                new("done", "Completed", "{\"done\":true}"), QueueWholeFlowStorage.Clock(fixture.Store));
            await Assert.That(JsonSerializer.Serialize(page.Events.Single(), JsonDefaults.Options))
                .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
            var output = fixture.Database.InspectMessage("root", lane, "output")!;
            await Assert.That(output.PayloadJson).IsEqualTo("{\"next\":true}");
            await Assert.That(output.HeadersJson).IsEqualTo("{}");
            await Assert.That(output.Metadata.Id).IsEqualTo("output");
            await Assert.That(output.Metadata.State).IsEqualTo(MessageState.Ready);
            await Assert.That(output.Metadata.Attempts).IsEqualTo(0);
            await Assert.That(output.Metadata.LeaseVersion).IsEqualTo(0L);
            await Assert.That(output.Metadata.LeaseUntil).IsNull();
        }
    }
}
