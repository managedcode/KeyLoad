using System.Text.Json;
using TUnit.Assertions.Exceptions;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class JsonTextQueueReplayTests
{
    private const string QueueName = "jobs";
    private const string RootPrincipal = "root";
    private const string WorkerPrincipal = "worker";
    private const string Tenant = "tenant";
    private const string Database = "database";
    private const string MessageBody = "{}";
    private const string FirstMessageId = "first";
    private const string SecondMessageId = "second";
    private const string RawSpellingSuffix = " ";
    private const int MultiDeliveryCount = 2;

    [Test]
    public async Task AcMp006MultiQueueReceiveReplayRetainsTokensAndRawSpellingIdentity()
    {
        using var db = new TestDatabase();
        db.Configure(QueueName, ResourceKind.WorkQueue);
        db.Commit(new EnqueueMessage(QueueName, FirstMessageId, MessageBody),
            new EnqueueMessage(QueueName, SecondMessageId, MessageBody));
        var now = TimeProvider.System.GetUtcNow().AddSeconds(1);
        var lane = new QueueLaneRef(db.Partition, QueueName);
        var id = Guid.NewGuid();
        var request = new ReceiveRequest(id, lane, MaxMessages: MultiDeliveryCount);
        var original = db.Submit(OperationKind.Receive, request, id: id, time: now);
        var replay = db.Submit(OperationKind.Receive, request, id: id, time: now);
        var deliveries = original.Get<ReceiveResult>().Deliveries;
        var changedSpelling = JsonSerializer.Serialize(request, JsonDefaults.Options) + RawSpellingSuffix;
        var conflict = db.Database.Apply(new(id, OperationKind.Receive, RootPrincipal, now, changedSpelling));

        await Assert.That(deliveries.Length).IsEqualTo(MultiDeliveryCount);
        await Assert.That(deliveries[0].Id).IsEqualTo(FirstMessageId);
        await Assert.That(deliveries[1].Id).IsEqualTo(SecondMessageId);
        await NativeReplayResultAssertions.Same<ReceiveResult>(replay, original);
        await Assert.That(conflict.Error).IsEqualTo(ErrorCode.Conflict);

        var ack = Guid.NewGuid();
        db.Submit(OperationKind.Delivery, new DeliveryCommand(ack, lane, deliveries[0].Token, DeliveryAction.Ack),
            id: ack, time: now).Get<CommitReceipt>();
        await Assert.That(db.Submit(OperationKind.Receive, request, id: id, time: now).Error)
            .IsEqualTo(ErrorCode.StaleLease);
    }

    [Test]
    public async Task AcMp006EmptyQueueReceiveReplayStillReauthorizesPrincipal()
    {
        using var db = new TestDatabase();
        db.Configure(QueueName, ResourceKind.WorkQueue);
        var worker = new PrincipalRecord(WorkerPrincipal, Tenant,
            [new(Database, QueueName, Capability.QueueConsume)], []);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(worker)).Get<PrincipalRecord>();
        var now = TimeProvider.System.GetUtcNow().AddSeconds(1);
        var id = Guid.NewGuid();
        var request = new ReceiveRequest(id, new(db.Partition, QueueName));
        var original = db.Submit(OperationKind.Receive, request, principal: WorkerPrincipal, id: id, time: now);
        var replay = db.Submit(OperationKind.Receive, request, principal: WorkerPrincipal, id: id, time: now);

        await Assert.That(original.Get<ReceiveResult>().Deliveries).IsEmpty();
        await NativeReplayResultAssertions.Same<ReceiveResult>(replay, original);

        db.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(worker with { Revoked = true, PolicyEpoch = 2 }), time: now.AddSeconds(1))
            .Get<PrincipalRecord>();
        await Assert.That(db.Submit(OperationKind.Receive, request, principal: WorkerPrincipal,
            id: id, time: now.AddSeconds(1)).Error).IsEqualTo(ErrorCode.Unauthenticated);
    }

    [Test]
    [Arguments(nameof(ReceiveResult.Token))]
    [Arguments(nameof(ReceiveResult.Deliveries))]
    [Arguments(nameof(Delivery.PayloadJson))]
    public async Task AcR14ReplayComparisonRejectsChangedTokenOrderedElementOrExactBody(string field)
    {
        using var db = new TestDatabase();
        db.Configure(QueueName, ResourceKind.WorkQueue);
        db.Commit(new EnqueueMessage(QueueName, FirstMessageId, MessageBody),
            new EnqueueMessage(QueueName, SecondMessageId, MessageBody));
        var id = Guid.NewGuid();
        var original = db.Submit(OperationKind.Receive,
            new ReceiveRequest(id, new(db.Partition, QueueName), MaxMessages: MultiDeliveryCount), id: id);
        var result = original.Get<ReceiveResult>();
        var changed = field switch
        {
            nameof(ReceiveResult.Token) => result with { Token = result.Token with { Position = result.Token.Position + 1 } },
            nameof(ReceiveResult.Deliveries) => result with { Deliveries = [result.Deliveries[1], result.Deliveries[0]] },
            nameof(Delivery.PayloadJson) => result with
            {
                Deliveries = [result.Deliveries[0] with { PayloadJson = result.Deliveries[0].PayloadJson + RawSpellingSuffix },
                    result.Deliveries[1]]
            },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        await Assert.ThrowsExactlyAsync<AssertionException>(() => NativeReplayResultAssertions.Same<ReceiveResult>(
            original with { NativeValue = changed }, original));
    }
}
