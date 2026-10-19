using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueStoredCapacityWholeFlow
{
    internal static async Task RunAsync(QueueStoredCapacityFault fault, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var fixture = new TestDatabase();
        var state = await QueueStoredCapacitySetup.SeedAsync(fixture, token);
        await QueueEnqueueColdAssertions.RequireReceiptAsync(state.Original.Get<CommitReceipt>(), state.Producer,
            fixture.Store.Identity.Incarnation);
        await QueueEnqueueColdAssertions.RequireInitialAsync(fixture.Database, fixture.Partition, state.ScheduledAt);
        var originalImage = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = fixture.Store.Identity;
        var rows = QueueStoredCapacityRows.Damage(fixture.Store, fixture.Partition, fault);
        var damagedImage = QueueWholeFlowStorage.Bytes(fixture.Store);
        await QueueStoredCapacityAssertions.RefuseAsync(fixture.Database, fixture.Store, fixture.Partition, state, fault, position + QueueEnqueueAuthorityProtocol.First, damagedImage);
        await QueueStoredCapacityAssertions.RefuseAsync(fixture.Database, fixture.Store, fixture.Partition, state, fault, position + QueueEnqueueAuthorityProtocol.First, damagedImage);
        fixture.Store.Dispose();
        using var cold = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        var owner = fixture.CreateConfiguredDatabase(cold);
        await ReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(cold, identity, position + QueueEnqueueAuthorityProtocol.First);
        await QueueStoredCapacityAssertions.RefuseAsync(owner, cold, fixture.Partition, state, fault, position + QueueEnqueueAuthorityProtocol.First, damagedImage);
        QueueStoredCapacityRows.Repair(cold, rows);
        await QueueStoredCapacityAssertions.ImageAsync(cold, position + QueueEnqueueAuthorityProtocol.Second, originalImage);
        await QueueEnqueueColdAssertions.RequireInitialAsync(owner, fixture.Partition, state.ScheduledAt);
        await ReplayResultAssertions.Same<CommitReceipt>(owner.Apply(QueueStoredCapacitySetup.Operation(
            owner, OperationKind.Batch, state.Producer, state.Producer.CommandId, new DateTimeOffset(state.Enqueue.Timestamp))), state.Original);
        await QueueStoredCapacityAssertions.ImageAsync(cold, position + QueueEnqueueAuthorityProtocol.Second, originalImage);
        await ContinueAsync(fixture, cold, owner, state, identity, token);
    }

    private static async Task ContinueAsync(TestDatabase fixture, ZoneTreeStore cold, DatabaseEngine owner,
        QueueStoredCapacityState state, StoreIdentity identity, CancellationToken token)
    {
        var laneImage = QueueEnqueueColdAssertions.LaneBytes(cold, fixture.Partition);
        var rejected = owner.Apply(state.Enqueue);
        await Assert.That(Errors.Code(rejected.Problem)).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(rejected.Value).IsNull();
        await Assert.That(owner.GetDocument(QueueEnqueueColdProtocol.Root, new(fixture.Partition, QueueEnqueueColdProtocol.Collection, QueueEnqueueColdProtocol.RefusedDocument), cancellationToken: token)).IsNull();
        await Assert.That(QueueEnqueueColdAssertions.LaneBytes(cold, fixture.Partition)).IsEquivalentTo(laneImage, CollectionOrdering.Matching);
        await QueueStoredCapacityReceipts.FailureAsync(owner, cold, fixture.Partition, state.Enqueue, rejected);
        var rejectedImage = QueueWholeFlowStorage.Bytes(cold);
        var rejectedPosition = cold.Position;
        await Assert.That(OrleansSerialization.Serialize(owner.Apply(state.Enqueue)).SequenceEqual(OrleansSerialization.Serialize(rejected))).IsTrue();
        await QueueStoredCapacityAssertions.ImageAsync(cold, rejectedPosition, rejectedImage);
        var expectedClaim = await QueueStoredCapacityReceipts.NextAsync(owner, cold, fixture.Partition);
        var claimed = owner.Apply(state.Receive);
        var delivery = await QueueStoredCapacityReceipts.ClaimAsync(owner, claimed, state, fixture.Partition, expectedClaim);
        await QueueStoredCapacityStateAssertions.LeasedAsync(owner, cold, fixture.Partition, delivery);
        await Assert.That(delivery.Id).IsEqualTo(QueueEnqueueColdProtocol.Ready);
        var lane = new QueueLaneRef(fixture.Partition, QueueEnqueueColdProtocol.Queue);
        var ack = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
        var expectedAck = await QueueStoredCapacityReceipts.NextAsync(owner, cold, fixture.Partition);
        var acknowledgement = owner.Apply(QueueStoredCapacitySetup.Operation(owner, OperationKind.Delivery, ack,
            ack.CommandId, new DateTimeOffset(state.Enqueue.Timestamp)));
        await QueueStoredCapacityReceipts.ReceiptAsync(acknowledgement, ack.CommandId, expectedAck,
            [new(nameof(DeliveryAction.Ack), lane.Queue, QueueEnqueueColdProtocol.Ready, QueueEnqueueAuthorityProtocol.Settled)]);
        await QueueStoredCapacityStateAssertions.TerminalAsync(owner, cold, fixture.Partition);
        var healthy = new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new PutDocument(QueueEnqueueColdProtocol.Collection, QueueEnqueueColdProtocol.RefusedDocument, QueueEnqueueColdProtocol.HealthyJson),
                new EnqueueMessage(QueueEnqueueColdProtocol.Queue, QueueEnqueueColdProtocol.Healthy, QueueEnqueueColdProtocol.HealthyJson)]);
        var healthyOperation = QueueStoredCapacitySetup.Operation(owner, OperationKind.Batch, healthy,
            healthy.CommandId, new DateTimeOffset(state.Enqueue.Timestamp));
        var expectedHealthy = await QueueStoredCapacityReceipts.NextAsync(owner, cold, fixture.Partition);
        var healthyResult = owner.Apply(healthyOperation);
        await QueueStoredCapacityReceipts.ReceiptAsync(healthyResult, healthy.CommandId, expectedHealthy,
            [new(QueueEnqueueColdProtocol.PutKind, QueueEnqueueColdProtocol.Collection, QueueEnqueueColdProtocol.RefusedDocument, QueueEnqueueAuthorityProtocol.First),
                new(QueueEnqueueColdProtocol.EnqueueKind, lane.Queue, QueueEnqueueColdProtocol.Healthy, QueueEnqueueAuthorityProtocol.First)]);
        await QueueEnqueueColdAssertions.RequireHealthyAsync(owner, fixture.Partition, state.ScheduledAt);
        await Assert.That(owner.GetDocument(QueueEnqueueColdProtocol.Root, new(fixture.Partition, QueueEnqueueColdProtocol.Collection, QueueEnqueueColdProtocol.RefusedDocument), cancellationToken: token)!.Json)
            .IsEqualTo(QueueEnqueueColdProtocol.HealthyJson);
        var ackOperation = QueueStoredCapacitySetup.Operation(owner, OperationKind.Delivery, ack,
            ack.CommandId, new DateTimeOffset(state.Enqueue.Timestamp));
        await SecondColdAsync(fixture, cold, state, identity, rejected, healthyOperation, healthyResult,
            ackOperation, acknowledgement, token);
    }

    private static async Task SecondColdAsync(TestDatabase fixture, ZoneTreeStore cold, QueueStoredCapacityState state,
        StoreIdentity identity, Result<object> rejected, Command<DatabaseCommand> healthyOperation, Result<object> healthyResult,
        Command<DatabaseCommand> ackOperation, Result<object> acknowledgement, CancellationToken token)
    {
        var finalImage = QueueWholeFlowStorage.Bytes(cold);
        var finalPosition = cold.Position;
        cold.Dispose();
        using var second = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        var secondOwner = fixture.CreateConfiguredDatabase(second);
        token.ThrowIfCancellationRequested();
        await ReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(second, identity, finalPosition);
        await QueueStoredCapacityAssertions.ImageAsync(second, finalPosition, finalImage);
        await QueueEnqueueColdAssertions.RequireHealthyAsync(secondOwner, fixture.Partition, state.ScheduledAt);
        await QueueStoredCapacityStateAssertions.TerminalAsync(secondOwner, second, fixture.Partition);
        await Assert.That(OrleansSerialization.Serialize(secondOwner.Apply(state.Enqueue)).SequenceEqual(OrleansSerialization.Serialize(rejected))).IsTrue();
        await QueueStoredCapacityReceipts.FailureAsync(secondOwner, second, fixture.Partition, state.Enqueue, rejected);
        await ReplayResultAssertions.Same<CommitReceipt>(secondOwner.Apply(healthyOperation), healthyResult);
        await ReplayResultAssertions.Same<CommitReceipt>(secondOwner.Apply(ackOperation), acknowledgement);
        await QueueStoredCapacityAssertions.ImageAsync(second, finalPosition, finalImage);
    }
}
