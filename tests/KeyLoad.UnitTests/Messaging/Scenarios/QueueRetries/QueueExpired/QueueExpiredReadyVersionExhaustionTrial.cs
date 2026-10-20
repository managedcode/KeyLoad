using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueExpiredReadyVersionExhaustionTrial
{
    internal static async Task RunAsync(QueueClaimVersionExhaustion field, CancellationToken token)
    {
        using var fixture = new TestDatabase();
        var state = await SeedAsync(fixture, field);
        var domain = QueueClaimVersionExhaustionAssertions.Domain(fixture.Store, state.Lane);
        var expected = await QueueExpiredReadyVersionExhaustionAssertions.NextTokenAsync(fixture.Database, fixture.Store, state.Lane);
        var before = fixture.Store.Position;
        var result = fixture.Database.Apply(state.Operation);
        if (field == QueueClaimVersionExhaustion.StateVersion)
        {
            await QueueClaimVersionExhaustionAssertions.FailureAsync(result);
            await Assert.That(fixture.Store.Position).IsEqualTo(before + 1);
            await QueueExpiredReadyVersionExhaustionAssertions.CurrentAsync(fixture.Database, fixture.Store, state.Lane,
                state.DamagedMetadata, state.Body, 1);
            await QueueClaimVersionExhaustionAssertions.DomainAsync(fixture.Store, state.Lane, domain);
        }
        else
        {
            await QueueExpiredReadyVersionExhaustionAssertions.EmptyAsync(result, state.Operation.CommandId, expected);
            await QueueExpiredReadyVersionExhaustionAssertions.CurrentAsync(fixture.Database, fixture.Store, state.Lane,
                QueueExpiredReadyVersionExhaustionAssertions.Expired(state.DamagedMetadata), null, 1);
        }
        await QueueClaimVersionExhaustionAssertions.ReplayAsync(fixture.Database, fixture.Store, state, result);
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
        await QueueExpiredReadyVersionExhaustionContinuation.RunAsync(QueueWholeFlowStorage.Open(cold), cold,
            fixture.Directory, state, result, field, identity, token);
    }

    private static async Task<QueueClaimVersionExhaustionState> SeedAsync(TestDatabase fixture, QueueClaimVersionExhaustion field)
    {
        fixture.Configure(QueueClaimVersionExhaustionProtocol.Queue, ResourceKind.WorkQueue);
        var lane = new QueueLaneRef(fixture.Partition, QueueClaimVersionExhaustionProtocol.Queue);
        var time = QueueWholeFlowStorage.Clock(fixture.Store);
        var message = new EnqueueMessage(lane.Queue, QueueClaimVersionExhaustionProtocol.Message,
            QueueClaimVersionExhaustionProtocol.Payload, QueueClaimVersionExhaustionProtocol.Headers, ExpiresAt: time.AddSeconds(1));
        var producer = new CommandRequest(Guid.NewGuid(), lane.Partition, [message]);
        var expected = await QueueExpiredReadyVersionExhaustionAssertions.NextTokenAsync(fixture.Database, fixture.Store, lane);
        var produced = QueueWholeFlowStorage.Apply(fixture.Database, OperationKind.Batch, producer, producer.CommandId, time);
        await QueueExpiredReadyVersionExhaustionAssertions.ReceiptAsync(produced, producer.CommandId, expected,
            QueueEnqueueColdProtocol.EnqueueKind, lane, message.MessageId, 1);
        var metadata = new MessageMetadata(message.MessageId, MessageState.Ready, 0, 1, 1, null, message.ExpiresAt);
        await Assert.That(fixture.Database.InspectMessage(QueueClaimVersionExhaustionProtocol.Root, lane, message.MessageId))
            .IsEqualTo(new MessageInspection(metadata, message.PayloadJson, message.HeadersJson));
        var key = KeySpace.Partition(QueueClaimVersionExhaustionProtocol.MetadataSpace, lane.Partition, lane.Queue, message.MessageId);
        var row = fixture.Store.Read(view => view.ReadOwnedValue(key))!;
        var maximum = field switch
        {
            QueueClaimVersionExhaustion.StateVersion => metadata with
            {
                StateVersion = long.MaxValue
            },
            QueueClaimVersionExhaustion.Attempts => metadata with
            {
                Attempts = int.MaxValue
            },
            QueueClaimVersionExhaustion.LeaseVersion => metadata with
            {
                LeaseVersion = long.MaxValue
            },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        fixture.Store.Commit((tx, _) =>
        {
            tx.PutRecord(key, maximum);
            return true;
        });
        var request = new ReceiveRequest(Guid.NewGuid(), lane, MaxMessages: 1, LeaseSeconds: QueueClaimVersionExhaustionProtocol.LeaseSeconds);
        var operation = fixture.Database.OwnCommand(QueueLeaseAuthoritySetup.Create(fixture.Database, OperationKind.Receive,
            request, request.RequestId, time.AddSeconds(2)));
        var body = new MessageBody(message.MessageId, message.PayloadJson, message.HeadersJson, message.OrderingKey, JsonData.Fingerprint(message));
        return new(lane, time.AddSeconds(2), producer, produced, key, row, maximum, body, operation);
    }
}
