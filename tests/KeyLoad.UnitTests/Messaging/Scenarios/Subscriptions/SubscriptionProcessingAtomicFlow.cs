using System.Text.Json;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionProcessingAtomicFlow
{
    internal static async Task RunAsync(CancellationToken token, SubscriptionDeliveryAuthorityFault? fault = null)
    {
        using var fixture = new TestDatabase();
        fixture.Configure("topic", ResourceKind.Topic);
        fixture.Configure("orders", ResourceKind.Collection);
        SubscriptionTestActions.Publish(fixture);
        var group = SubscriptionTestActions.Configure(fixture, policy: new()
        {
            MaxWindow = 3
        });
        var source = SubscriptionCheckpointAuthorityAssertions.SourceImage(fixture.Store, group.Source.Partition);
        var time = QueueWholeFlowStorage.Clock(fixture.Store).AddSeconds(1);
        var receive = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 3);
        var claim = Create(fixture.Database, OperationKind.ReceiveSubscription, receive, receive.RequestId, time);
        var originalClaim = fixture.Database.Apply(claim);
        var deliveries = originalClaim.Get<ReceiveSubscriptionResult>().Deliveries;
        await Assert.That(deliveries.Select(d => d.Event.Position)).IsEquivalentTo(
            new long[] { 1, 2, 3 }, CollectionOrdering.Matching);
        await ReplayResultAssertions.Same<ReceiveSubscriptionResult>(fixture.Database.Apply(claim), originalClaim);
        var request = new SubscriptionProcessingRequest(Guid.NewGuid(), group, deliveries[0].Token, "worker", 1,
            [new PutDocument("orders", "first", "{\"n\":1}", 0),
             new PutDocument("orders", "second", "{\"n\":2}", 9)]);
        var failed = Create(fixture.Database, OperationKind.SubscriptionProcessing, request, request.CommandId, time.AddSeconds(1));
        if (fault is { } selected)
        {
            await CorruptSubscriptionProcessingAssertions.RefuseAndRestoreAsync(fixture, group, failed, selected);
        }
        var before = SubscriptionProcessingAtomicAssertions.DomainImage(fixture.Store, group, failed.CommandId);
        var position = fixture.Store.Position;
        var canonicalPosition = fixture.Database.LastApplied;
        var failure = fixture.Database.Apply(failed);
        await SubscriptionProcessingAtomicAssertions.RollbackAsync(fixture.Database, fixture.Store,
            group, failed, failure, before, position + 1);
        await Assert.That(fixture.Database.LastApplied).IsEqualTo(canonicalPosition + 1);
        var failureBytes = SubscriptionProcessingAtomicAssertions.OutcomeBytes(fixture.Store, group, failed.CommandId);
        await SubscriptionProcessingAtomicAssertions.FailureReplayAsync(fixture.Database, fixture.Store,
            group, failed, failure, failureBytes);
        var cut = QueueWholeFlowStorage.Bytes(fixture.Store);
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        position = fixture.Store.Position;
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        using var first = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var db = QueueWholeFlowStorage.Open(first);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(first, cut, position, identity);
        await SubscriptionProcessingAtomicAssertions.FailureReplayAsync(db, first, group, failed, failure, failureBytes);
        await ReplayResultAssertions.Same<ReceiveSubscriptionResult>(db.Apply(claim), originalClaim);
        await SubscriptionCheckpointAuthorityAssertions.SourceAsync(db, first, group.Source, source);
        await ContinueAsync(db, first, fixture.Directory, group, request, failed, failure,
            failureBytes, claim, originalClaim, time, source, fixture, token);
    }

    private static async Task ContinueAsync(DatabaseEngine db, ZoneTreeStore store, string directory,
        SubscriptionRef group, SubscriptionProcessingRequest request, Command<DatabaseCommand> failed, Result<object> failure,
        byte[] failureBytes, Command<DatabaseCommand> claim, Result<object> originalClaim, DateTimeOffset time,
        string[] source, TestDatabase originalOwner, CancellationToken token)
    {
        var repaired = request with
        {
            CommandId = Guid.NewGuid(),
            Effects = [new PutDocument("orders", "first", "{\"n\":1}", 0),
                       new PutDocument("orders", "second", "{\"n\":2}", 0)]
        };
        var operation = Create(db, OperationKind.SubscriptionProcessing, repaired, repaired.CommandId, time.AddSeconds(2));
        var result = db.Apply(operation);
        await SubscriptionProcessingAtomicAssertions.SuccessAsync(db, group, repaired, result, originalOwner);
        var saved = SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, group, operation.CommandId);
        await SubscriptionProcessingAtomicAssertions.SuccessReplayAsync(db, store, group, operation, result, saved);
        await SubscriptionProcessingAtomicAssertions.FailureReplayAsync(db, store, group, failed, failure, failureBytes);
        var once = repaired with
        {
            CommandId = Guid.NewGuid()
        };
        var onceOperation = Create(db, OperationKind.SubscriptionProcessing, once, once.CommandId, time.AddSeconds(3));
        var beforeOnce = SubscriptionProcessingAtomicAssertions.DomainImage(store, group, onceOperation.CommandId);
        var oncePosition = store.Position;
        var onceCanonical = db.LastApplied;
        var onceResult = db.Apply(onceOperation);
        await SubscriptionProcessingAtomicAssertions.OwnOutcomeOnlyAsync(store, group, onceOperation, beforeOnce, oncePosition + 1);
        await Assert.That(db.LastApplied).IsEqualTo(onceCanonical + 1);
        var projected = onceResult.Get<SubscriptionProcessingResult>();
        await Assert.That(projected.AlreadyProcessed).IsTrue();
        await Assert.That(projected.OriginalEffectsToken).IsEqualTo(result.Get<SubscriptionProcessingResult>().OriginalEffectsToken);
        await Assert.That(JsonSerializer.Serialize(projected.Receipt, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(result.Get<SubscriptionProcessingResult>().Receipt, JsonDefaults.Options));
        await SubscriptionProcessingAtomicAssertions.DocumentsAsync(db, group, originalOwner);
        var receipts = new List<(Command<DatabaseCommand> Operation, Result<object> Result, byte[] Saved)>
        { (operation, result, saved), (onceOperation, onceResult,
            SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, group, onceOperation.CommandId)) };
        var ackReceipts = new List<(SubscriptionDeliveryCommand Command, Result<object> Result)>();
        var deliveries = originalClaim.Get<ReceiveSubscriptionResult>().Deliveries;
        foreach (var delivery in deliveries.Skip(1))
        {
            var command = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, delivery.Token, DeliveryAction.Ack);
            var ack = Create(db, OperationKind.SubscriptionDelivery, command, command.CommandId, time.AddSeconds(4));
            var receipt = db.Apply(ack);
            await SubscriptionCheckpointAuthorityAssertions.ReceiptAsync(receipt, command, delivery.Event.Position);
            await SubscriptionCheckpointAuthorityAssertions.ReplayAsync(db, store, command, receipt, token);
            ackReceipts.Add((command, receipt));
        }
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, 3, 3, 3);
        await SubscriptionCheckpointAuthorityAssertions.NoWindowAsync(store, group);
        var receive = OrleansSerialization.Deserialize<ReceiveSubscriptionRequest>(claim.Value!.Input.Span, ValidationProfile.PublicInputElements)
            ?? throw new InvalidOperationException("The original Receive request is absent.");
        await SubscriptionCheckpointAuthorityAssertions.SettledClaimAsync(db, store, receive, originalClaim, token);
        await SecondColdAsync(db, store, directory, group, failed, failure, failureBytes, receipts, ackReceipts, receive, originalClaim, source, originalOwner, token);
    }

    private static async Task SecondColdAsync(DatabaseEngine db, ZoneTreeStore store, string directory,
        SubscriptionRef group, Command<DatabaseCommand> failed, Result<object> failure, byte[] failureBytes,
        List<(Command<DatabaseCommand> Operation, Result<object> Result, byte[] Saved)> receipts,
        List<(SubscriptionDeliveryCommand Command, Result<object> Result)> ackReceipts,
        ReceiveSubscriptionRequest receive, Result<object> originalClaim, string[] source, TestDatabase originalOwner, CancellationToken token)
    {
        await SubscriptionCheckpointAuthorityAssertions.SourceAsync(db, store, group.Source, source);
        var cut = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        store.Dispose();
        token.ThrowIfCancellationRequested();
        using var second = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var cold = QueueWholeFlowStorage.Open(second);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(second, cut, position, identity);
        await SubscriptionProcessingAtomicAssertions.FailureReplayAsync(cold, second, group, failed, failure, failureBytes);
        foreach (var receipt in receipts)
        {
            await SubscriptionProcessingAtomicAssertions.SuccessReplayAsync(cold, second, group,
            receipt.Operation, receipt.Result, receipt.Saved);
        }
        foreach (var ack in ackReceipts)
        {
            await SubscriptionCheckpointAuthorityAssertions.ReplayAsync(cold, second, ack.Command, ack.Result, token);
        }
        await SubscriptionCheckpointAuthorityAssertions.SettledClaimAsync(cold, second, receive, originalClaim, token);
        await SubscriptionProcessingAtomicAssertions.DocumentsAsync(cold, group, originalOwner);
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(cold, group, 3, 3, 3);
        await SubscriptionCheckpointAuthorityAssertions.NoWindowAsync(second, group);
        await SubscriptionCheckpointAuthorityAssertions.SourceAsync(cold, second, group.Source, source);
        await SubscriptionReceiveSlotAuthorityAssertions.HealthyAsync(cold, second, group,
            QueueWholeFlowStorage.Clock(second).AddSeconds(1), token);
    }

    private static Command<DatabaseCommand> Create<T>(DatabaseEngine database, OperationKind kind, T payload, Guid id, DateTimeOffset time)
        => database.CreateCommand(kind, id, "root", time, JsonSerializer.Serialize(payload, JsonDefaults.Options));
}

