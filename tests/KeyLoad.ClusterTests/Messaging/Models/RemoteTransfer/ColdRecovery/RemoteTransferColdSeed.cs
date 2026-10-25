
namespace KeyLoad.ClusterTests.Messaging;

internal sealed record RemoteTransferColdSeed(MessagingClusterScenario Scenario,
    MessagingClusterIdentity Identity, Guid TransferId, CommandRequest Create, EnqueueMessage Message)
{
    internal InspectQueueTransferRequest SourceRequest => new(Scenario.SourceQueue, TransferId);
    internal InspectQueueTransferReceiptRequest ReceiptRequest => new(Scenario.DestinationQueue,
        Scenario.SourceQueue, TransferId);
    internal CommandRequest Accept(QueueTransferInspection intent) => new(Guid.NewGuid(),
        Scenario.DestinationPartition, [new AcceptQueueTransfer(Scenario.DestinationQueue, intent.IntentToken)]);
    internal CommandRequest Complete(QueueTransferReceiptInspection receipt) => new(Guid.NewGuid(),
        Scenario.SourcePartition, [new CompleteQueueTransfer(Scenario.SourceQueue, TransferId, receipt.ReceiptToken)]);
}
