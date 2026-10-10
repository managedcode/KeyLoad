using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class QueueOrderedRetryCrashSeed
{
    internal static (DatabaseEngine Database, Delivery Delivery) Seed(ZoneTreeStore store)
    {
        var database = CrashDatabase.Create(store, tenantId: QueueOrderedRetryCrashProtocol.Partition.TenantId);
        var resource = new ResourceDefinition(QueueOrderedRetryCrashProtocol.Queue, ResourceKind.WorkQueue,
            QueueOrderedRetryCrashProtocol.Partition.TransactionDomainId)
        { QueuePolicy = new() { OrderingProfile = QueueOrderingProfile.StrictPerKey, RetryJitter = QueueRetryJitter.Full } };
        _ = CrashDatabase.Submit(database, OperationKind.ConfigureResource, new ConfigureResourceRequest(
            QueueOrderedRetryCrashProtocol.Partition.TenantId, QueueOrderedRetryCrashProtocol.Partition.DatabaseId, resource), Guid.NewGuid())
            .Get<ResourceDefinition>();
        var id = Guid.NewGuid();
        _ = CrashDatabase.Submit(database, OperationKind.Batch, new CommandRequest(id, QueueOrderedRetryCrashProtocol.Partition,
            [Enqueue(QueueOrderedRetryCrashProtocol.First), Enqueue(QueueOrderedRetryCrashProtocol.Second)]), id).Get<CommitReceipt>();
        var request = new ReceiveRequest(Guid.NewGuid(), new(QueueOrderedRetryCrashProtocol.Partition, QueueOrderedRetryCrashProtocol.Queue),
            MaxMessages: QueueOrderedRetryCrashProtocol.One);
        var delivery = CrashDatabase.Submit(database, OperationKind.Receive, request, request.RequestId).Get<ReceiveResult>().Deliveries.Single();
        if (delivery.Id != QueueOrderedRetryCrashProtocol.First || delivery.PayloadJson != QueueOrderedRetryCrashProtocol.Payload
            || delivery.HeadersJson != QueueOrderedRetryCrashProtocol.Headers)
        { throw new InvalidOperationException(QueueOrderedRetryCrashProtocol.SeedMismatch); }
        return (database, delivery);
    }

    internal static EnqueueMessage Enqueue(string id) => new(QueueOrderedRetryCrashProtocol.Queue, id,
        QueueOrderedRetryCrashProtocol.Payload, QueueOrderedRetryCrashProtocol.Headers, OrderingKey: QueueOrderedRetryCrashProtocol.Key);
}
