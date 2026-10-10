using KeyLoad.Core;
namespace KeyLoad.CrashHost.Features.Messaging;

internal static class RemoteTransferAttemptCrashSetup
{
    internal static void Configure(DatabaseEngine database)
    {
        foreach (var lane in new[] { RemoteTransferAttemptCrashScenario.Source, RemoteTransferAttemptCrashScenario.Destination })
        {
            var resource = new ResourceDefinition(lane.Queue, ResourceKind.WorkQueue, lane.Partition.TransactionDomainId);
            if (lane == RemoteTransferAttemptCrashScenario.Destination)
            { resource = resource with { QueuePolicy = new() { MaxStoredMessages = RemoteTransferAttemptCrashProtocol.SingleStoredMessage } }; }
            _ = CrashDatabase.Submit(database, OperationKind.ConfigureResource,
                new ConfigureResourceRequest(lane.Partition.TenantId, lane.Partition.DatabaseId, resource), Guid.NewGuid()).Get<ResourceDefinition>();
        }
        var id = Guid.NewGuid();
        _ = CrashDatabase.Submit(database, OperationKind.Batch,
            new CommandRequest(id, RemoteTransferAttemptCrashScenario.Destination.Partition,
                [new EnqueueMessage(RemoteTransferAttemptCrashScenario.Destination.Queue,
                    RemoteTransferAttemptCrashProtocol.FillerMessage, RemoteTransferAttemptCrashProtocol.Payload)]), id).Get<CommitReceipt>();
    }

    internal static void AckFiller(DatabaseEngine database)
    {
        var receive = new ReceiveRequest(Guid.NewGuid(), RemoteTransferAttemptCrashScenario.Destination);
        var delivery = CrashDatabase.Submit(database, OperationKind.Receive, receive, receive.RequestId).Get<ReceiveResult>().Deliveries.Single();
        if (delivery.Id != RemoteTransferAttemptCrashProtocol.FillerMessage)
        { throw new InvalidOperationException(RemoteTransferAttemptCrashProtocol.Missing); }
        var ack = new DeliveryCommand(Guid.NewGuid(), RemoteTransferAttemptCrashScenario.Destination, delivery.Token, DeliveryAction.Ack);
        _ = CrashDatabase.Submit(database, OperationKind.Delivery, ack, ack.CommandId).Get<CommitReceipt>();
    }
}
