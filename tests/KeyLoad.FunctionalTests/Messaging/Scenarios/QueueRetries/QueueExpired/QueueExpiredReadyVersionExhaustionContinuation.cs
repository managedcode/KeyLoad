using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueExpiredReadyVersionExhaustionContinuation
{
    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store, string root,
        QueueClaimVersionExhaustionState state, Result<object> original, QueueClaimVersionExhaustion field,
        byte[] identity, CancellationToken token)
    {
        await QueueClaimVersionExhaustionAssertions.ReplayAsync(database, store, state, original);
        var metadata = QueueExpiredReadyVersionExhaustionAssertions.Expired(state.DamagedMetadata);
        var successes = new List<(Command<DatabaseCommand> Operation, Result<object> Result)>();
        if (field == QueueClaimVersionExhaustion.StateVersion)
        {
            await QueueExpiredReadyVersionExhaustionAssertions.CurrentAsync(database, store, state.Lane, state.DamagedMetadata, state.Body, 1);
            store.Commit((tx, _) =>
            {
                tx.Put(state.MetadataKey, state.OriginalMetadata);
                return true;
            });
            var repaired = OrleansSerialization.Deserialize<MessageMetadata>(state.OriginalMetadata);
            await QueueExpiredReadyVersionExhaustionAssertions.CurrentAsync(database, store, state.Lane, repaired, state.Body, 1);
            await QueueClaimVersionExhaustionAssertions.ReplayAsync(database, store, state, original);
            var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, MaxMessages: 1,
                LeaseSeconds: QueueClaimVersionExhaustionProtocol.LeaseSeconds);
            var operation = database.OwnCommand(QueueLeaseAuthoritySetup.Create(database, OperationKind.Receive, request, request.RequestId, state.Time));
            var expected = await QueueExpiredReadyVersionExhaustionAssertions.NextTokenAsync(database, state.Lane);
            var expired = database.Apply(operation);
            await QueueExpiredReadyVersionExhaustionAssertions.EmptyAsync(expired, request.RequestId, expected);
            successes.Add((operation, expired));
            metadata = QueueExpiredReadyVersionExhaustionAssertions.Expired(repaired);
        }
        await QueueExpiredReadyVersionExhaustionAssertions.CurrentAsync(database, store, state.Lane, metadata, null, 1);
        await QueueClaimVersionExhaustionAssertions.ReplayAsync(database, store, state, original);
        var healthyClaim = await HealthyAsync(database, store, state, successes);
        await QueueExpiredReadyVersionExhaustionAssertions.CurrentAsync(database, store, state.Lane, metadata, null, 2);
        await QueueClaimVersionExhaustionAssertions.ReplayAsync(database, store, state, original);
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        store.Dispose();
        token.ThrowIfCancellationRequested();
        using var cold = new ZoneTreeStore(new(root), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var restored = QueueWholeFlowStorage.Open(cold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
        await ReplayColdAsync(restored, cold, state, original, metadata, successes, healthyClaim);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
    }

    private static async Task<(Command<DatabaseCommand> Operation, Result<object> Result)> HealthyAsync(DatabaseEngine database,
        ZoneTreeStore store, QueueClaimVersionExhaustionState state, List<(Command<DatabaseCommand> Operation, Result<object> Result)> successes)
    {
        const string id = "healthy";
        const string payload = "{\"input\":2}";
        const string headers = "{\"kind\":\"healthy\"}";
        var message = new EnqueueMessage(state.Lane.Queue, id, payload, headers);
        var body = new MessageBody(id, payload, headers, null, JsonData.Fingerprint(message));
        var command = new CommandRequest(Guid.NewGuid(), state.Lane.Partition, [message]);
        var producer = database.OwnCommand(QueueLeaseAuthoritySetup.Create(database, OperationKind.Batch, command, command.CommandId, state.Time));
        var expectedProducer = await QueueExpiredReadyVersionExhaustionAssertions.NextTokenAsync(database, state.Lane);
        var produced = database.Apply(producer);
        await QueueExpiredReadyVersionExhaustionAssertions.ReceiptAsync(produced, command.CommandId, expectedProducer,
            QueueEnqueueColdProtocol.EnqueueKind, state.Lane, id, 1);
        successes.Add((producer, produced));
        await QueueExpiredReadyVersionExhaustionAssertions.CurrentAsync(database, store, state.Lane,
            new(id, MessageState.Ready, 0, 1, 2, null, null), body, 2);
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, MaxMessages: 1,
            LeaseSeconds: QueueClaimVersionExhaustionProtocol.LeaseSeconds);
        var claim = database.OwnCommand(QueueLeaseAuthoritySetup.Create(database, OperationKind.Receive, request, request.RequestId, state.Time));
        var expectedClaim = await QueueExpiredReadyVersionExhaustionAssertions.NextTokenAsync(database, state.Lane);
        var claimed = database.Apply(claim);
        var delivery = await Assert.That(claimed.Get<ReceiveResult>().Deliveries).HasSingleItem();
        await QueueClaimVersionExhaustionAssertions.EqualAsync(delivery, new Delivery(id, payload, headers, delivery.Token,
            1, state.Time.AddSeconds(QueueClaimVersionExhaustionProtocol.LeaseSeconds), 1, 1));
        await Assert.That(delivery.Token).IsNotEqualTo(string.Empty);
        await QueueClaimVersionExhaustionAssertions.EqualAsync(database.Verify<DeliveryClaims>(delivery.Token),
            new DeliveryClaims(state.Lane, id, QueueClaimVersionExhaustionProtocol.Root, 1, 1, expectedClaim.Incarnation));
        await QueueClaimVersionExhaustionAssertions.EqualAsync(claimed.Get<ReceiveResult>(),
            new ReceiveResult(request.RequestId, [new(id, payload, headers, delivery.Token, 1,
                state.Time.AddSeconds(QueueClaimVersionExhaustionProtocol.LeaseSeconds), 1, 1)], expectedClaim));
        await QueueExpiredReadyVersionExhaustionAssertions.CurrentAsync(database, store, state.Lane,
            new(id, MessageState.Leased, 1, 2, 2, null, null, QueueClaimVersionExhaustionProtocol.Root, 1, delivery.LeaseUntil), body, 2);
        await QueueClaimVersionExhaustionAssertions.EqualAsync(database.Apply(claim), claimed);
        var ack = new DeliveryCommand(Guid.NewGuid(), state.Lane, delivery.Token, DeliveryAction.Ack);
        var completion = database.OwnCommand(QueueLeaseAuthoritySetup.Create(database, OperationKind.Delivery, ack, ack.CommandId, state.Time));
        var expectedAck = await QueueExpiredReadyVersionExhaustionAssertions.NextTokenAsync(database, state.Lane);
        var receipt = database.Apply(completion);
        await QueueExpiredReadyVersionExhaustionAssertions.ReceiptAsync(receipt, ack.CommandId, expectedAck,
            nameof(DeliveryAction.Ack), state.Lane, id, 3);
        successes.Add((completion, receipt));
        await QueueExpiredReadyVersionExhaustionAssertions.CurrentAsync(database, store, state.Lane,
            new(id, MessageState.Acked, 1, 3, 2, null, null, LeaseVersion: 1), null, 2);
        return (claim, claimed);
    }

    private static async Task ReplayColdAsync(DatabaseEngine database, ZoneTreeStore store, QueueClaimVersionExhaustionState state,
        Result<object> original, MessageMetadata expired, List<(Command<DatabaseCommand> Operation, Result<object> Result)> successes,
        (Command<DatabaseCommand> Operation, Result<object> Result) claim)
    {
        foreach (var pair in successes)
        {
            await QueueClaimVersionExhaustionAssertions.EqualAsync(database.Apply(pair.Operation), pair.Result);
        }
        await QueueClaimVersionExhaustionAssertions.EqualAsync(QueueWholeFlowStorage.Apply(database, OperationKind.Batch,
            state.Producer, state.Producer.CommandId, state.Time), state.Produced);
        await QueueClaimVersionExhaustionAssertions.ReplayAsync(database, store, state, original);
        var consumed = database.Apply(claim.Operation);
        await Assert.That(Errors.Code(consumed.Problem).Value).IsEqualTo(ErrorCode.StaleLease);

        await Assert.That(consumed.Value).IsNull();
        var retained = store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(
            state.Lane.Partition, QueueClaimVersionExhaustionProtocol.Root, claim.Operation.CommandId)))!;
        await QueueClaimVersionExhaustionAssertions.EqualAsync(retained.Result, claim.Result);
        await QueueExpiredReadyVersionExhaustionAssertions.CurrentAsync(database, store, state.Lane, expired, null, 2);
        await QueueExpiredReadyVersionExhaustionAssertions.CurrentAsync(database, store, state.Lane,
            new("healthy", MessageState.Acked, 1, 3, 2, null, null, LeaseVersion: 1), null, 2);
    }
}
