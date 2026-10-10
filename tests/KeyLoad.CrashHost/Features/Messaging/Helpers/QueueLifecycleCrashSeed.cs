using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class QueueLifecycleCrashSeed
{
    private const string NativeReferenceOrderingKey = "native-reference";
    private const string UnexpectedDeliveryOrder = "The native lifecycle seed delivery order differs.";
    internal static DatabaseEngine Seed(ZoneTreeStore store)
    {
        var database = CrashDatabase.Create(store, tenantId: QueueLifecycleCrashProtocol.Partition.TenantId);
        var resource = new ResourceDefinition(QueueLifecycleCrashProtocol.Queue, ResourceKind.WorkQueue,
            QueueLifecycleCrashProtocol.Partition.TransactionDomainId)
        {
            QueuePolicy = new()
            {
                MaxAttempts = QueueLifecycleCrashProtocol.One,
                MaxStoredMessages = QueueLifecycleCrashProtocol.Two,
                MaxDeadLetterMessages = QueueLifecycleCrashProtocol.One
            }
        };
        _ = CrashDatabase.Submit(database, OperationKind.ConfigureResource, new ConfigureResourceRequest(
            QueueLifecycleCrashProtocol.Partition.TenantId, QueueLifecycleCrashProtocol.Partition.DatabaseId, resource), Guid.NewGuid())
            .Get<ResourceDefinition>();
        var id = Guid.NewGuid();
        _ = CrashDatabase.Submit(database, OperationKind.Batch, new CommandRequest(id, QueueLifecycleCrashProtocol.Partition,
            [Enqueue(QueueLifecycleCrashProtocol.Parked), Enqueue(QueueLifecycleCrashProtocol.Pending)]), id).Get<CommitReceipt>();
        Exhaust(database, QueueLifecycleCrashProtocol.Parked);
        Exhaust(database, QueueLifecycleCrashProtocol.Pending);
        return database;
    }

    internal static EnqueueMessage Enqueue(string id) => new(QueueLifecycleCrashProtocol.Queue, id,
        QueueLifecycleCrashProtocol.Payload, QueueLifecycleCrashProtocol.Headers, OrderingKey: NativeReferenceOrderingKey);

    private static void Exhaust(DatabaseEngine database, string expected)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), new(QueueLifecycleCrashProtocol.Partition, QueueLifecycleCrashProtocol.Queue),
            LeaseSeconds: QueueLifecycleCrashProtocol.LeaseSeconds);
        var received = CrashDatabase.Submit(database, OperationKind.Receive, request, request.RequestId).Get<ReceiveResult>();
        var delivery = received.Deliveries.Single();
        if (delivery.Id != expected)
        { throw new InvalidOperationException(UnexpectedDeliveryOrder); }
        var nack = new DeliveryCommand(Guid.NewGuid(), request.Lane, delivery.Token, DeliveryAction.Nack);
        _ = CrashDatabase.Submit(database, OperationKind.Delivery, nack, nack.CommandId).Get<CommitReceipt>();
    }
}
