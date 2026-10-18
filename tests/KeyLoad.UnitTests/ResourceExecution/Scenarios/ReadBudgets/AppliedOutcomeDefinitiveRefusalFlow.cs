using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class AppliedOutcomeDefinitiveRefusalFlow
{
    internal const string Queue = "definitive-refusal";
    internal const string Worker = "definitive-worker";
    internal const string Message = "original";
    internal const string Healthy = "healthy";
    internal const string Payload = "{\"work\":1}";
    internal const string Headers = "{\"kind\":\"outcome\"}";

    internal static async Task RunAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var fixture = new TestDatabase();
        var (lane, original, changed, committed) = await SeedAsync(fixture, token);
        await AppliedOutcomeDefinitiveRefusalAssertions.CurrentAsync(fixture.Database, fixture.Store, lane, false);
        await AppliedOutcomeDefinitiveRefusalAssertions.ConflictAsync(fixture.Database, fixture.Store, original, changed, committed);
        token.ThrowIfCancellationRequested();
        await AppliedOutcomeDefinitiveRefusalAssertions.BoundariesAsync(fixture.Database, fixture.Store, fixture.Partition, original, changed, committed);
        await ContinueColdAsync(fixture, lane, original, changed, committed, token);
    }

    private static async Task<(QueueLaneRef Lane, Command<DatabaseCommand> Original, Command<DatabaseCommand> Changed, Result<object> Committed)>
        SeedAsync(TestDatabase fixture, CancellationToken token)
    {
        fixture.Configure(Queue, ResourceKind.WorkQueue);
        fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(
            new(Worker, fixture.Partition.TenantId,
                [new(fixture.Partition.DatabaseId, Queue, Capability.QueueConsume | Capability.QueueAck)], [])))
            .Get<PrincipalRecord>();
        var producer = new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new EnqueueMessage(Queue, Message, Payload, Headers), new EnqueueMessage(Queue, Healthy, Payload, Headers)]);
        fixture.Submit(OperationKind.Batch, producer, id: producer.CommandId).Get<CommitReceipt>();
        var lane = new QueueLaneRef(fixture.Partition, Queue);
        var claim = new ReceiveRequest(Guid.NewGuid(), lane, MaxMessages: 1);
        var received = fixture.Database.Apply(Issue(fixture.Database, OperationKind.Receive, claim, claim.RequestId))
            .Get<ReceiveResult>();
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(Message);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        token.ThrowIfCancellationRequested();
        var ack = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
        var original = Issue(fixture.Database, OperationKind.Delivery, ack, ack.CommandId);
        var expected = ExpectedCanonicalCommit.Next(fixture.Database, lane.Partition);
        var committed = fixture.Database.Apply(original);
        await AppliedOutcomeDefinitiveRefusalAssertions.ReceiptAsync(committed, ack.CommandId, expected, Message);
        var changed = Issue(fixture.Database, OperationKind.Delivery, ack with
        {
            Action = DeliveryAction.Nack
        }, ack.CommandId);
        return (lane, original, changed, committed);
    }

    private static async Task ContinueColdAsync(TestDatabase fixture, QueueLaneRef lane, Command<DatabaseCommand> original,
        Command<DatabaseCommand> changed, Result<object> committed, CancellationToken token)
    {
        var bytes = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var db = QueueWholeFlowStorage.Open(cold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, bytes, position, identity);
        await AppliedOutcomeDefinitiveRefusalAssertions.CurrentAsync(db, cold, lane, false);
        await AppliedOutcomeDefinitiveRefusalAssertions.ConflictAsync(db, cold, original, changed, committed);
        await HealthyAsync(db, lane, token);
        await AppliedOutcomeDefinitiveRefusalAssertions.CurrentAsync(db, cold, lane, true);
        await AppliedOutcomeDefinitiveRefusalAssertions.ConflictAsync(db, cold, original, changed, committed);
        await FinalColdAsync(fixture.Directory, cold, lane, original, changed, committed, token);
    }

    private static async Task FinalColdAsync(string directory, ZoneTreeStore store, QueueLaneRef lane,
        Command<DatabaseCommand> original, Command<DatabaseCommand> changed, Result<object> committed, CancellationToken token)
    {
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        store.Dispose();
        token.ThrowIfCancellationRequested();
        using var cold = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var db = QueueWholeFlowStorage.Open(cold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, bytes, position, identity);
        await AppliedOutcomeDefinitiveRefusalAssertions.CurrentAsync(db, cold, lane, true);
        await AppliedOutcomeDefinitiveRefusalAssertions.ConflictAsync(db, cold, original, changed, committed);
    }

    private static async Task HealthyAsync(DatabaseEngine db, QueueLaneRef lane, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var request = new ReceiveRequest(Guid.NewGuid(), lane, MaxMessages: 1);
        var received = db.Apply(Issue(db, OperationKind.Receive, request, request.RequestId)).Get<ReceiveResult>();
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(Healthy);
        await Assert.That(delivery.PayloadJson).IsEqualTo(Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(Headers);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        var ack = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
        var issued = Issue(db, OperationKind.Delivery, ack, ack.CommandId);
        var expected = ExpectedCanonicalCommit.Next(db, lane.Partition);
        var committed = db.Apply(issued);
        await AppliedOutcomeDefinitiveRefusalAssertions.ReceiptAsync(committed, ack.CommandId, expected, Healthy);
        await NativeReplayResultAssertions.Same<CommitReceipt>(db.ResolveAppliedSubmissionOutcome(issued), committed);
        request = request with
        {
            RequestId = Guid.NewGuid()
        };
        await Assert.That(db.Apply(Issue(db, OperationKind.Receive, request, request.RequestId)).Get<ReceiveResult>().Deliveries).IsEmpty();
    }

    internal static Command<DatabaseCommand> Issue<T>(DatabaseEngine db, OperationKind kind, T payload, Guid id)
        => db.CreateCommand(kind, id, Worker, db.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(payload));
}
