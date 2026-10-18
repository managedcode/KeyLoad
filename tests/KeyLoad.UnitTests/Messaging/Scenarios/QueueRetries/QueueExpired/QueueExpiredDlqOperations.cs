using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueExpiredDlqOperations
{
    private const int ExpiryAdvanceSeconds = 1;
    private const string Witness = "expired-retained-witness";

    internal static async Task<QueueExpiredDlqState> SeedAsync(TestDatabase fixture, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        QueueLifecycleProvisioning.Seed(fixture, false);
        var state = new QueueLifecycleTestState(fixture.Partition, QueueWholeFlowStorage.Clock(fixture.Store));
        var expiry = state.Time.AddSeconds(ExpiryAdvanceSeconds);
        state.Batch(fixture.Database, new PutDocument(QueueLifecycleTestProtocol.Collection, Witness, QueueLifecycleTestProtocol.Payload),
            QueueLifecycleHealthy.Enqueue(QueueLifecycleTestProtocol.Parked) with
            {
                ExpiresAt = expiry
            },
            QueueLifecycleHealthy.Enqueue(QueueLifecycleTestProtocol.Pending) with
            {
                ExpiresAt = expiry
            }).Get<CommitReceipt>();
        var parked = await QueueLifecycleOperations.ClaimAsync(fixture.Database, state, QueueLifecycleTestProtocol.Parked);
        QueueLifecycleOperations.Complete(fixture.Database, state, parked, DeliveryAction.Nack).Get<CommitReceipt>();
        var pending = await QueueLifecycleOperations.ClaimAsync(fixture.Database, state, QueueLifecycleTestProtocol.Pending);
        QueueLifecycleOperations.Complete(fixture.Database, state, pending, DeliveryAction.Nack).Get<CommitReceipt>();
        var result = new QueueExpiredDlqState(state with
        {
            Time = expiry
        }, expiry,
            QueueExpiredDlqAssertions.ReadBody(fixture.Store, state, QueueLifecycleTestProtocol.Parked),
            QueueExpiredDlqAssertions.ReadBody(fixture.Store, state, QueueLifecycleTestProtocol.Pending));
        await QueueExpiredDlqAssertions.RetainedAsync(fixture.Database, result, false);
        return result;
    }

    internal static async Task RefusalsAsync(DatabaseEngine database, global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, QueueExpiredDlqState state, bool parkedCancelled)
    {
        var operations = state.Operations;
        var image = QueueLifecycleImage.Capture(store, operations.Lane);
        var empty = new ReceiveRequest(Guid.NewGuid(), operations.Lane);
        await Assert.That(operations.Execute(database, OperationKind.Receive, empty, empty.RequestId)
            .Get<ReceiveResult>().Deliveries).IsEmpty();
        await QueueLifecycleImage.SameAsync(store, operations.Lane, image);
        foreach (var id in parkedCancelled ? new[] { QueueLifecycleTestProtocol.Pending }
            : new[] { QueueLifecycleTestProtocol.Parked, QueueLifecycleTestProtocol.Pending })
        {
            await QueueExpiredDlqAssertions.RefusalAsync(database, store, operations,
                new RedriveQueueMessage(operations.Lane.Queue, id, 3L, 1L), ErrorCode.Validation);
        }
        await QueueExpiredDlqAssertions.RefusalAsync(database, store, operations,
            new ParkPendingQueueMessage(operations.Lane.Queue, QueueLifecycleTestProtocol.Pending, 3L, 1L), ErrorCode.Validation);
        await QueueExpiredDlqAssertions.RetainedAsync(database, state, parkedCancelled);
        await QueueLifecycleOperations.ReplayAsync(database, operations);
    }

    internal static async Task RollbackAsync(DatabaseEngine database, global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, QueueExpiredDlqState state)
    {
        var operations = state.Operations;
        var before = QueueLifecycleImage.Capture(store, operations.Lane);
        var originalWitness = database.GetDocument(QueueLifecycleTestProtocol.Administrator,
            new(operations.Partition, QueueLifecycleTestProtocol.Collection, Witness))!;
        await Assert.That(originalWitness.Json).IsEqualTo(QueueLifecycleTestProtocol.Payload);
        await Assert.That(originalWitness.Revision).IsEqualTo(1L);
        var witness = JsonSerializer.Serialize(database.GetDocument(QueueLifecycleTestProtocol.Administrator,
            new(operations.Partition, QueueLifecycleTestProtocol.Collection, Witness)), JsonDefaults.Options);
        var actual = operations.Batch(database,
            new CancelQueueMessage(operations.Lane.Queue, QueueLifecycleTestProtocol.Parked, 3L, 1L),
            new PutDocument(QueueLifecycleTestProtocol.Collection, Witness, "{\"knowledge\":\"must not commit\"}", ExpectedRevision: 0L));
        await Assert.That(Errors.Code(actual.Problem)).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(actual.Value).IsNull();
        await QueueLifecycleImage.SameAsync(store, operations.Lane, before);
        await Assert.That(JsonSerializer.Serialize(database.GetDocument(QueueLifecycleTestProtocol.Administrator,
            new(operations.Partition, QueueLifecycleTestProtocol.Collection, Witness)), JsonDefaults.Options)).IsEqualTo(witness);
        await QueueExpiredDlqAssertions.RetainedAsync(database, state, false);
        await QueueLifecycleOperations.ReplayAsync(database, operations);
    }

    internal static async Task HealthyAsync(DatabaseEngine database, QueueExpiredDlqState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var operations = state.Operations;
        operations.Batch(database, QueueLifecycleHealthy.Enqueue(QueueLifecycleTestProtocol.Healthy)).Get<CommitReceipt>();
        var delivery = await QueueLifecycleOperations.ClaimAsync(database, operations, QueueLifecycleTestProtocol.Healthy);
        QueueLifecycleOperations.Complete(database, operations, delivery, DeliveryAction.Ack).Get<CommitReceipt>();
        await QueueExpiredDlqAssertions.MetadataAsync(database, operations, QueueLifecycleTestProtocol.Healthy,
            new(QueueLifecycleTestProtocol.Healthy, MessageState.Acked, 1, 3L, 3L, null, null, LeaseVersion: 1L));
        await QueueExpiredDlqAssertions.TerminalAsync(database, state, true);
        await QueueLifecycleOperations.ReplayAsync(database, operations);
    }
}
