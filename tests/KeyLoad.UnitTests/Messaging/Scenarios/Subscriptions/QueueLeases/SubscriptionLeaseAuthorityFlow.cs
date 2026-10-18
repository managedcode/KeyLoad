using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;
namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionLeaseAuthorityFlow
{
    internal static async Task RunAsync(DeliveryAction action, CancellationToken token,
        SubscriptionDeliveryAuthorityFault fault = SubscriptionDeliveryAuthorityFault.ForeignPosition)
    {
        using var fixture = new TestDatabase();
        fixture.Configure("topic", ResourceKind.Topic);
        SubscriptionTestActions.Publish(fixture);
        var group = SubscriptionTestActions.Configure(fixture, policy: new()
        {
            MaxWindow = 3
        });
        var source = SubscriptionCheckpointAuthorityAssertions.SourceImage(fixture.Store, group.Source.Partition);
        var time = QueueWholeFlowStorage.Clock(fixture.Store).AddSeconds(1);
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 3, LeaseSeconds: 30);
        var operation = Create(fixture.Database, OperationKind.ReceiveSubscription, request, request.RequestId, time);
        var original = fixture.Database.Apply(operation);
        var deliveries = original.Get<ReceiveSubscriptionResult>().Deliveries;
        await Assert.That(deliveries.Select(d => d.Event.Position)).IsEquivalentTo(new long[] { 1, 2, 3 }, CollectionOrdering.Matching);
        await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(fixture.Database.Apply(operation), original);
        var command = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, deliveries[1].Token, action, LeaseSeconds: 20);
        var attempt = Create(fixture.Database, OperationKind.SubscriptionDelivery, command, command.CommandId, time.AddSeconds(1));
        var key = SubscriptionCheckpointAuthorityFlow.WindowKey(group, 2);
        var saved = fixture.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The genuine leased subscription slot is absent.");
        var row = NativeSerialization.Deserialize<GroupDelivery>(saved);
        var originalBytes = QueueWholeFlowStorage.Bytes(fixture.Store);
        var originalPosition = fixture.Store.Position;
        fixture.Store.Commit((tx, _) =>
        {
            SubscriptionDeliveryAuthorityFaults.Stage(tx, key, row, fault);
            return true;
        });
        await SubscriptionLeaseAuthorityAssertions.RefusalAsync(fixture.Database, fixture.Store, operation, original, attempt);
        var bytes = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        using var first = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var db = QueueWholeFlowStorage.Open(first);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(first, bytes, position, identity);
        await SubscriptionLeaseAuthorityAssertions.RefusalAsync(db, first, operation, original, attempt);
        await SubscriptionCheckpointAuthorityAssertions.SourceAsync(db, first, group.Source, source);
        first.Commit((tx, _) =>
        {
            tx.Put(key, saved);
            return true;
        });
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(first, originalBytes, originalPosition + 2, identity);
        await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(db.Apply(operation), original);
        await CompleteAsync(db, first, fixture.Directory, group, action, row, key, attempt,
            command, operation, original, time, source, token);
    }

    private static async Task CompleteAsync(DatabaseEngine db, ZoneTreeStore store, string directory,
        SubscriptionRef group, DeliveryAction action, GroupDelivery row, byte[] key, Command<DatabaseCommand> attempt,
        SubscriptionDeliveryCommand command, Command<DatabaseCommand> claim, Result<object> original,
        DateTimeOffset time, string[] source, CancellationToken token)
    {
        var result = db.Apply(attempt);
        await SubscriptionLeaseAuthorityAssertions.ReceiptAsync(result, command, 2L, 1L);
        var expected = action switch
        {
            DeliveryAction.Ack => row with { State = GroupDeliveryState.Acked, LeaseUntil = null, PrincipalId = null },
            DeliveryAction.Renew => row with { LeaseUntil = time.AddSeconds(21) },
            DeliveryAction.Nack => row with
            {
                State = GroupDeliveryState.Pending,
                AvailableAt = time.AddSeconds(2),
                LeaseUntil = null,
                PrincipalId = null
            },
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        await Assert.That(store.Read(view => view.ReadOwnedValue(key))!.AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        await Assert.That(store.Read(view => view.ReadOwnedValue(SubscriptionCheckpointAuthorityFlow.WindowKey(group, 99)))).IsNull();
        var receipts = new List<(Command<DatabaseCommand> Operation, Result<object> Result)> { (attempt, result) };
        var second = original.Get<ReceiveSubscriptionResult>().Deliveries[1];
        Command<DatabaseCommand>? reclaimed = null;
        Result<object>? reclaimResult = null;
        if (action == DeliveryAction.Nack)
        {
            var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 3, LeaseSeconds: 30);
            reclaimed = Create(db, OperationKind.ReceiveSubscription, request, request.RequestId, time.AddSeconds(3));
            var reclaimedOutcome = db.Apply(reclaimed);
            reclaimResult = reclaimedOutcome;
            second = await Assert.That(reclaimedOutcome.Get<ReceiveSubscriptionResult>().Deliveries).HasSingleItem();
            await Assert.That(second).IsEqualTo(new SubscriptionDelivery(original.Get<ReceiveSubscriptionResult>().Deliveries[1].Event,
                second.Token, 2L, time.AddSeconds(33), 2));
            await Assert.That(second.Token).IsNotEqualTo(command.Token);
            await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(db.Apply(reclaimed), reclaimedOutcome);
        }
        else if (action == DeliveryAction.Renew)
        {
            await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(db.Apply(claim), original);
        }
        if (action != DeliveryAction.Ack)
        {
            await AckAsync(db, group, second.Token, time.AddSeconds(3), 2L, action == DeliveryAction.Nack ? 2L : 1L, receipts);
        }
        await AckAsync(db, group, original.Get<ReceiveSubscriptionResult>().Deliveries[2].Token, time.AddSeconds(3), 3L, 1L, receipts);
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, 0, 3, 3);
        await AckAsync(db, group, original.Get<ReceiveSubscriptionResult>().Deliveries[0].Token, time.AddSeconds(3), 1L, 1L, receipts);
        await SubscriptionLeaseAuthorityAssertions.SettledAsync(db, store, group, claim, original, receipts, reclaimed, reclaimResult);
        await ColdAsync(store, directory, group, source, claim, original, receipts, reclaimed, reclaimResult, token);
    }

    private static async Task ColdAsync(ZoneTreeStore store, string directory, SubscriptionRef group, string[] source,
        Command<DatabaseCommand> claim, Result<object> original, List<(Command<DatabaseCommand> Operation, Result<object> Result)> receipts,
        Command<DatabaseCommand>? reclaimed, Result<object>? reclaimResult, CancellationToken token)
    {
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        store.Dispose();
        token.ThrowIfCancellationRequested();
        using var second = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var db = QueueWholeFlowStorage.Open(second);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(second, bytes, position, identity);
        await SubscriptionCheckpointAuthorityAssertions.SourceAsync(db, second, group.Source, source);
        await SubscriptionLeaseAuthorityAssertions.SettledAsync(db, second, group, claim, original, receipts, reclaimed, reclaimResult);
        await SubscriptionReceiveSlotAuthorityAssertions.HealthyAsync(db, second, group,
            QueueWholeFlowStorage.Clock(second).AddSeconds(1), token);
    }

    private static async Task AckAsync(DatabaseEngine db, SubscriptionRef group, string leaseToken, DateTimeOffset time,
        long position, long leaseVersion, List<(Command<DatabaseCommand> Operation, Result<object> Result)> receipts)
    {
        var command = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, leaseToken, DeliveryAction.Ack);
        var operation = Create(db, OperationKind.SubscriptionDelivery, command, command.CommandId, time);
        var result = db.Apply(operation);
        await SubscriptionLeaseAuthorityAssertions.ReceiptAsync(result, command, position, leaseVersion);
        receipts.Add((operation, result));
    }

    private static Command<DatabaseCommand> Create<T>(DatabaseEngine database, OperationKind kind, T payload, Guid id, DateTimeOffset time)
        => database.CreateCommand(kind, id, "root", time, JsonSerializer.Serialize(payload, JsonDefaults.Options));
}
