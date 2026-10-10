using KeyLoad.Core;
namespace KeyLoad.UnitTests.Features.Messaging;

internal static class TargetInboxNativeSetup
{
    internal static (QueueLaneRef Source, QueueLaneRef Target, Delivery Delivery, CommitInboxRequest Request) Create(TestDatabase fixture)
    {
        fixture.Configure(TargetInboxUnitProtocol.Input, ResourceKind.WorkQueue);
        fixture.Configure(TargetInboxUnitProtocol.Output, ResourceKind.WorkQueue);
        fixture.Configure(TargetInboxUnitProtocol.Collection, ResourceKind.Collection);
        fixture.Configure(TargetInboxUnitProtocol.Events, ResourceKind.StreamSet);
        var resource = new ResourceDefinition(TargetInboxUnitProtocol.Target, ResourceKind.WorkQueue, fixture.Partition.TransactionDomainId)
        { InboxPolicy = new(TargetInboxUnitProtocol.ReceiptCapacity, TargetInboxUnitProtocol.ByteCapacity) };
        _ = fixture.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId, resource)).Get<ResourceDefinition>();
        var source = new QueueLaneRef(fixture.Partition with { PartitionKey = TargetInboxUnitProtocol.SourcePartition }, TargetInboxUnitProtocol.Input);
        var producer = new CommandRequest(Guid.NewGuid(), source.Partition,
            [new EnqueueMessage(source.Queue, TargetInboxUnitProtocol.Message, TargetInboxUnitProtocol.Payload)]);
        _ = fixture.Submit(OperationKind.Batch, producer, id: producer.CommandId).Get<CommitReceipt>();
        var receive = new ReceiveRequest(Guid.NewGuid(), source);
        var delivery = fixture.Submit(OperationKind.Receive, receive, id: receive.RequestId).Get<ReceiveResult>().Deliveries.Single();
        var target = new QueueLaneRef(fixture.Partition, TargetInboxUnitProtocol.Target);
        var request = new CommitInboxRequest(Guid.NewGuid(), target, source, delivery.Id, delivery.DeliveryGeneration,
            TargetInboxUnitProtocol.Handler, TargetInboxUnitProtocol.Generation,
            [new PutDocument(TargetInboxUnitProtocol.Collection, TargetInboxUnitProtocol.Document, TargetInboxUnitProtocol.Payload,
                TargetInboxUnitProtocol.EmptyRevision),
             new AppendEvents(TargetInboxUnitProtocol.Events, TargetInboxUnitProtocol.Stream,
                [new(TargetInboxUnitProtocol.Message, TargetInboxUnitProtocol.EventType, TargetInboxUnitProtocol.Payload)], ExpectedStreamRevision.NoStream),
             new EnqueueMessage(TargetInboxUnitProtocol.Output, TargetInboxUnitProtocol.Message, TargetInboxUnitProtocol.Payload)]);
        return (source, target, delivery, request);
    }

    internal static OperationResult Apply(DatabaseEngine database, CommitInboxRequest request, CancellationToken token,
        string principal = TargetInboxUnitProtocol.Root)
        => database.ApplyEmbedded(new(request.CommandId, OperationKind.CommitInbox, principal, default,
            System.Text.Json.JsonSerializer.Serialize(request, JsonDefaults.Options)), token);
}
