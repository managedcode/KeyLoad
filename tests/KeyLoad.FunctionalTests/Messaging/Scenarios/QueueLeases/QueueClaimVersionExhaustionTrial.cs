using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueClaimVersionExhaustionTrial
{
    internal static async Task RunAsync(QueueClaimVersionExhaustion field, CancellationToken token)
    {
        using var fixture = new TestDatabase();
        var state = await SeedAsync(fixture, field);
        var domain = QueueClaimVersionExhaustionAssertions.Domain(fixture.Store, state.Lane);
        var before = fixture.Store.Position;
        var failed = fixture.Database.Apply(state.Operation);
        await QueueClaimVersionExhaustionAssertions.FailureAsync(failed);
        await Assert.That(fixture.Store.Position).IsEqualTo(before + 1);
        await QueueClaimVersionExhaustionAssertions.ReadyAsync(fixture.Database, fixture.Store, state);
        await QueueClaimVersionExhaustionAssertions.DomainAsync(fixture.Store, state.Lane, domain);
        await QueueClaimVersionExhaustionAssertions.ReplayAsync(fixture.Database, fixture.Store, state, failed);
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
        await QueueClaimVersionExhaustionColdRepair.RunAsync(QueueWholeFlowStorage.Open(cold), cold,
            fixture.Directory, state, failed, identity, token);
    }

    private static async Task<QueueClaimVersionExhaustionState> SeedAsync(TestDatabase fixture, QueueClaimVersionExhaustion field)
    {
        fixture.Configure(QueueClaimVersionExhaustionProtocol.Queue, ResourceKind.WorkQueue);
        var lane = new QueueLaneRef(fixture.Partition, QueueClaimVersionExhaustionProtocol.Queue);
        var time = QueueWholeFlowStorage.Clock(fixture.Store);
        var message = new EnqueueMessage(lane.Queue, QueueClaimVersionExhaustionProtocol.Message,
            QueueClaimVersionExhaustionProtocol.Payload, QueueClaimVersionExhaustionProtocol.Headers);
        var producer = new CommandRequest(Guid.NewGuid(), lane.Partition, [message]);
        var produced = QueueWholeFlowStorage.Apply(fixture.Database, OperationKind.Batch, producer, producer.CommandId, time);
        produced.Get<CommitReceipt>();
        var original = new MessageMetadata(message.MessageId, MessageState.Ready, 0, 1, 1, null, null);
        await Assert.That(fixture.Database.InspectMessage(QueueClaimVersionExhaustionProtocol.Root, lane, message.MessageId))
            .IsEqualTo(new MessageInspection(original, message.PayloadJson, message.HeadersJson));
        var key = KeySpace.Partition(QueueClaimVersionExhaustionProtocol.MetadataSpace, lane.Partition, lane.Queue, message.MessageId);
        var row = fixture.Store.Read(view => view.ReadOwnedValue(key))!;
        var damaged = field switch
        {
            QueueClaimVersionExhaustion.Attempts => original with
            {
                Attempts = int.MaxValue
            },
            QueueClaimVersionExhaustion.StateVersion => original with
            {
                StateVersion = long.MaxValue
            },
            QueueClaimVersionExhaustion.LeaseVersion => original with
            {
                LeaseVersion = long.MaxValue
            },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        fixture.Store.Commit((tx, _) =>
        {
            tx.PutRecord(key, damaged);
            return true;
        });
        var request = new ReceiveRequest(Guid.NewGuid(), lane, MaxMessages: 1, LeaseSeconds: QueueClaimVersionExhaustionProtocol.LeaseSeconds);
        var operation = fixture.Database.OwnCommand(QueueLeaseAuthoritySetup.Create(fixture.Database, OperationKind.Receive, request, request.RequestId, time));
        var body = new MessageBody(message.MessageId, message.PayloadJson, message.HeadersJson, message.OrderingKey, JsonData.Fingerprint(message));
        return new(lane, time, producer, produced, key, row, damaged, body, operation);
    }
}
