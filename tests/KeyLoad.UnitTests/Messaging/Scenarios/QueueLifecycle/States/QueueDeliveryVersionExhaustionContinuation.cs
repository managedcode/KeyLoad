using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeliveryVersionExhaustionContinuation
{
    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store, string root,
        QueueDeliveryVersionExhaustionState state, Result<object> failed, DeliveryAction action, byte[] identity, CancellationToken token)
    {
        var data = state.Data;
        await QueueDeliveryVersionExhaustionAssertions.LeasedAsync(database, store, state, data.DamagedMetadata);
        await QueueClaimVersionExhaustionAssertions.ReplayAsync(database, store, data, failed);
        store.Commit((tx, _) =>
        {
            tx.PutRecord(data.MetadataKey, state.RepairedMetadata);
            return true;
        });
        await QueueDeliveryVersionExhaustionAssertions.LeasedAsync(database, store, state, state.RepairedMetadata);
        await QueueClaimVersionExhaustionAssertions.ReplayAsync(database, store, data, failed);
        var receipts = new List<(Command<DatabaseCommand> Operation, Result<object> Result)>();
        var command = new DeliveryCommand(Guid.NewGuid(), data.Lane, state.Delivery.Token, action);
        var operation = database.OwnCommand(QueueLeaseAuthoritySetup.Create(database, OperationKind.Delivery, command,
            command.CommandId, new DateTimeOffset(data.Operation.Timestamp)));
        var result = database.Apply(operation);
        await QueueDeliveryVersionExhaustionAssertions.ReceiptAsync(result, command, data.Body.Id, 3);
        receipts.Add((operation, result));
        var terminal = await SettleOriginalAsync(database, store, state, action, receipts);
        await QueueDeliveryVersionExhaustionAssertions.TerminalAsync(database, store, state, terminal, false);
        await HealthyAsync(database, state, terminal.ReadySequence + 1, receipts);
        await QueueDeliveryVersionExhaustionAssertions.TerminalAsync(database, store, state, terminal, true);
        await QueueClaimVersionExhaustionAssertions.ReplayAsync(database, store, data, failed);
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        store.Dispose();
        token.ThrowIfCancellationRequested();
        using var cold = new ZoneTreeStore(new(root), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var restored = QueueWholeFlowStorage.Open(cold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
        await ReplayColdAsync(restored, cold, state, failed, terminal, receipts);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
    }

    private static async Task<MessageMetadata> SettleOriginalAsync(DatabaseEngine database, ZoneTreeStore store, QueueDeliveryVersionExhaustionState state,
        DeliveryAction action, List<(Command<DatabaseCommand> Operation, Result<object> Result)> receipts)
    {
        var data = state.Data;
        var at = new DateTimeOffset(data.Operation.Timestamp);
        var version = 3L;
        var delivery = state.Delivery;
        if (action == DeliveryAction.Renew)
        {
            await QueueDeliveryVersionExhaustionAssertions.LeasedAsync(database, store, state,
                state.RepairedMetadata with
                {
                    StateVersion = 3,
                    LeaseUntil = at.AddSeconds(QueueClaimVersionExhaustionProtocol.LeaseSeconds)
                });
        }
        if (action == DeliveryAction.Nack)
        {
            at = at.AddMilliseconds(QueueLeaseAuthorityProtocol.RetryMilliseconds);
            var expected = state.RepairedMetadata with
            {
                State = MessageState.Scheduled,
                StateVersion = version,
                LeaseOwner = null,
                LeaseUntil = null,
                NotBefore = at,
                SafeFailureCode = QueueLeaseAuthorityProtocol.RetryReason
            };
            await QueueDeliveryVersionExhaustionAssertions.ScheduledAsync(database, store, state, expected);
            delivery = await ClaimAsync(database, data.Lane, data.Body.Id, data.Body.PayloadJson, data.Body.HeadersJson, at, 2);
            version = 5;
        }
        if (action != DeliveryAction.Ack)
        {
            var command = new DeliveryCommand(Guid.NewGuid(), data.Lane, delivery.Token, DeliveryAction.Ack);
            var operation = database.OwnCommand(QueueLeaseAuthoritySetup.Create(database, OperationKind.Delivery, command, command.CommandId, at));
            var result = database.Apply(operation);
            version++;
            await QueueDeliveryVersionExhaustionAssertions.ReceiptAsync(result, command, data.Body.Id, version);
            receipts.Add((operation, result));
        }
        return new(data.Body.Id, MessageState.Acked, action == DeliveryAction.Nack ? 2 : 1, version, action == DeliveryAction.Nack ? 2 : 1, null, null,
            LeaseVersion: action == DeliveryAction.Nack ? 2 : 1, SafeFailureCode: action == DeliveryAction.Nack ? QueueLeaseAuthorityProtocol.RetryReason : null);
    }

    private static async Task HealthyAsync(DatabaseEngine database, QueueDeliveryVersionExhaustionState state, long sequence,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> receipts)
    {
        const string id = "healthy";
        const string payload = "{\"work\":2}";
        const string headers = "{\"kind\":\"healthy\"}";
        var data = state.Data;
        var at = new DateTimeOffset(data.Operation.Timestamp).AddSeconds(2);
        var request = new CommandRequest(Guid.NewGuid(), data.Lane.Partition, [new EnqueueMessage(data.Lane.Queue, id, payload, headers)]);
        var operation = database.OwnCommand(QueueLeaseAuthoritySetup.Create(database, OperationKind.Batch, request, request.CommandId, at));
        var created = database.Apply(operation);
        created.Get<CommitReceipt>();
        receipts.Add((operation, created));
        var delivery = await ClaimAsync(database, data.Lane, id, payload, headers, at, 1);
        var ack = new DeliveryCommand(Guid.NewGuid(), data.Lane, delivery.Token, DeliveryAction.Ack);
        var completion = database.OwnCommand(QueueLeaseAuthoritySetup.Create(database, OperationKind.Delivery, ack, ack.CommandId, at));
        var result = database.Apply(completion);
        await QueueDeliveryVersionExhaustionAssertions.ReceiptAsync(result, ack, id, 3);
        receipts.Add((completion, result));
        await Assert.That(database.InspectMessage(QueueClaimVersionExhaustionProtocol.Root, data.Lane, id))
            .IsEqualTo(new MessageInspection(new(id, MessageState.Acked, 1, 3, sequence, null, null, LeaseVersion: 1), null, null));
    }

    private static async Task<Delivery> ClaimAsync(DatabaseEngine database, QueueLaneRef lane, string id,
        string payload, string headers, DateTimeOffset at, int attempt)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), lane, MaxMessages: 1, LeaseSeconds: QueueClaimVersionExhaustionProtocol.LeaseSeconds);
        var operation = database.OwnCommand(QueueLeaseAuthoritySetup.Create(database, OperationKind.Receive, request, request.RequestId, at));
        var result = database.Apply(operation);
        var delivery = await Assert.That(result.Get<ReceiveResult>().Deliveries).HasSingleItem();
        await QueueClaimVersionExhaustionAssertions.EqualAsync(delivery, new Delivery(id, payload, headers, delivery.Token,
            attempt, at.AddSeconds(QueueClaimVersionExhaustionProtocol.LeaseSeconds), attempt, 1));
        var page = result.Get<ReceiveResult>();
        await Assert.That(page.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(page.Token.Incarnation).IsEqualTo(database.Store.Identity.Incarnation);
        await Assert.That(page.Token.AtomicPartitionId).IsEqualTo(lane.Partition.AtomicPartitionId);
        await Assert.That(page.Token.Position).IsGreaterThan(0L);
        await QueueClaimVersionExhaustionAssertions.EqualAsync(database.Apply(operation), result);
        return delivery;
    }

    private static async Task ReplayColdAsync(DatabaseEngine database, ZoneTreeStore store, QueueDeliveryVersionExhaustionState state,
        Result<object> failed, MessageMetadata terminal, List<(Command<DatabaseCommand> Operation, Result<object> Result)> receipts)
    {
        foreach (var pair in receipts)
        {
            await QueueClaimVersionExhaustionAssertions.EqualAsync(database.Apply(pair.Operation), pair.Result);
        }
        await QueueClaimVersionExhaustionAssertions.EqualAsync(QueueWholeFlowStorage.Apply(database, OperationKind.Batch,
            state.Data.Producer, state.Data.Producer.CommandId, state.Data.Time), state.Data.Produced);
        await QueueClaimVersionExhaustionAssertions.ReplayAsync(database, store, state.Data, failed);
        var stale = database.Apply(state.Claim);
        await Assert.That(Errors.Code(stale.Problem)).IsEqualTo(ErrorCode.StaleLease);
        await Assert.That(stale.Value).IsNull();

        var original = store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(
            state.Data.Lane.Partition, QueueClaimVersionExhaustionProtocol.Root, state.Claim.CommandId)))!;
        await QueueClaimVersionExhaustionAssertions.EqualAsync(original.Result, state.Claimed);
        await QueueDeliveryVersionExhaustionAssertions.TerminalAsync(database, store, state, terminal, true);
        await Assert.That(database.InspectMessage(QueueClaimVersionExhaustionProtocol.Root, state.Data.Lane, "healthy"))
            .IsEqualTo(new MessageInspection(new("healthy", MessageState.Acked, 1, 3, terminal.ReadySequence + 1, null, null, LeaseVersion: 1), null, null));
    }
}
