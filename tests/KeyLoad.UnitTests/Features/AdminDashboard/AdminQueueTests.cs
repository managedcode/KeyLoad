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
        var lane = SeedOverdueQueue(db);
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
        var observedAt = TimeProvider.System.GetUtcNow();
        await Assert.That(first.Items[0].LeaseUntil is { } lease && lease < observedAt).IsTrue();
        await Assert.That(next.Items[0].NotBefore is { } schedule && schedule < observedAt).IsTrue();
        await Assert.That(next.NextAfterId).IsNull();
        await Assert.That(first.Counters).IsEqualTo(next.Counters);
        await Assert.That(first.Counters.StoredMessages).IsEqualTo(2);
        await Assert.That(first.Counters.InFlightMessages).IsEqualTo(1);
        await Assert.That(first.CutPosition).IsEqualTo(position);
        await Assert.That(next.CutPosition).IsEqualTo(position);
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

    private static QueueLaneRef SeedOverdueQueue(TestDatabase db)
    {
        var configuredAt = TimeProvider.System.GetUtcNow().AddMinutes(-1);
        var resource = new ResourceDefinition(Queue, ResourceKind.WorkQueue, db.Partition.TransactionDomainId);
        db.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(db.Partition.TenantId,
            db.Partition.DatabaseId, resource), time: configuredAt).Get<ResourceDefinition>();
        var commandId = Guid.NewGuid();
        db.Submit(OperationKind.Batch, new CommandRequest(commandId, db.Partition,
            [new EnqueueMessage(Queue, First, Payload),
                new EnqueueMessage(Queue, Second, Payload, NotBefore: configuredAt.AddSeconds(4))]),
            id: commandId, time: configuredAt.AddSeconds(1)).Get<CommitReceipt>();
        var lane = new QueueLaneRef(db.Partition, Queue);
        var receiveId = Guid.NewGuid();
        db.Submit(OperationKind.Receive, new ReceiveRequest(receiveId, lane, LeaseSeconds: 1),
            id: receiveId, time: configuredAt.AddSeconds(2)).Get<ReceiveResult>();
        return lane;
    }
}
