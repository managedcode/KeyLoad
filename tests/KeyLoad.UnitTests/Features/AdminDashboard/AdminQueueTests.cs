using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.AdminDashboard;

internal sealed class AdminQueueTests
{
    private const string Administrator = "root";
    private const string Queue = "dashboard-jobs";
    private const string First = "first";
    private const string Second = "second";
    private const string SecretPayload = "private-message-body";
    private const string Payload = "{\"marker\":\"private-message-body\"}";

    [Test]
    public async Task AcAd005PaginationNeverConsumesOrSweepsPersistedMessages()
    {
        using var db = new TestDatabase();
        db.Configure(Queue, ResourceKind.WorkQueue);
        var now = TimeProvider.System.GetUtcNow().AddSeconds(-5);
        var commandId = Guid.NewGuid();
        db.Submit(OperationKind.Batch, new CommandRequest(commandId, db.Partition,
            [new EnqueueMessage(Queue, First, Payload),
                new EnqueueMessage(Queue, Second, Payload, NotBefore: now.AddSeconds(1))]), id: commandId, time: now)
            .Get<CommitReceipt>();
        var lane = new QueueLaneRef(db.Partition, Queue);
        var receiveId = Guid.NewGuid();
        db.Submit(OperationKind.Receive, new ReceiveRequest(receiveId, lane, LeaseSeconds: 1), id: receiveId, time: now)
            .Get<ReceiveResult>();
        var firstBefore = db.Database.InspectMessage(Administrator, lane, First);
        var secondBefore = db.Database.InspectMessage(Administrator, lane, Second);
        var position = db.Database.LastApplied;
        var reader = new AdminQueueReader(db.Database);
        var first = reader.Read(Administrator, new(lane, Limit: 1), CancellationToken.None);
        var next = reader.Read(Administrator, new(lane, first.NextAfterId, 1), CancellationToken.None);
        await Assert.That(first.Items[0].Id).IsEqualTo(First);
        await Assert.That(first.Items[0].State).IsEqualTo(MessageState.Leased);
        await Assert.That(next.Items[0].Id).IsEqualTo(Second);
        await Assert.That(next.Items[0].State).IsEqualTo(MessageState.Scheduled);
        await Assert.That(next.NextAfterId).IsNull();
        await Assert.That(first.Counters).IsEqualTo(next.Counters);
        await Assert.That(first.Counters.StoredMessages).IsEqualTo(2);
        await Assert.That(first.Counters.InFlightMessages).IsEqualTo(1);
        await Assert.That(db.Database.LastApplied).IsEqualTo(position);
        await Assert.That(db.Database.InspectMessage(Administrator, lane, First)).IsEqualTo(firstBefore);
        await Assert.That(db.Database.InspectMessage(Administrator, lane, Second)).IsEqualTo(secondBefore);
        await Assert.That(JsonSerializer.Serialize(first, JsonDefaults.Options).Contains(SecretPayload, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task AcAd005EmptyQueueAndInvalidPageHaveExplicitResults()
    {
        using var db = new TestDatabase();
        db.Configure(Queue, ResourceKind.WorkQueue);
        var reader = new AdminQueueReader(db.Database);
        var request = new AdminQueueRequest(new(db.Partition, Queue));
        var page = reader.Read(Administrator, request, CancellationToken.None);
        await Assert.That(page.Items).IsEmpty();
        await Assert.That(page.Counters.StoredMessages).IsEqualTo(0);
        await Assert.That(page.NextAfterId).IsNull();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => reader.Read(Administrator,
            request with { Limit = 0 }, CancellationToken.None)).Code).IsEqualTo(ErrorCode.Validation);
    }
}
