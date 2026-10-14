namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class LocalProcessingLateR53State(QueueLaneRef lane, MessagingRf3Identity identity)
{
    internal QueueLaneRef Lane { get; } = lane;
    internal MessagingRf3Identity Identity { get; } = identity;
    internal QueueLaneRef Output => new(Lane.Partition, InboxProcessingRf3Protocol.Output);
    internal EntityRef Document => new(Lane.Partition, InboxProcessingRf3Protocol.Collection, LocalProcessingLateR53Protocol.Document);
    internal ProcessingRequest Failed { get; set; } = null!;
    internal ManagedCode.Communication.Result<CommitReceipt> OriginalFailure
    {
        get; set;
    }
    internal ProcessingRequest Success { get; set; } = null!;
    internal CommitReceipt? Receipt
    {
        get; set;
    }
    internal MessageInspection Input { get; set; } = null!;
    internal MessageInspection? OutputImage
    {
        get; set;
    }
    internal DocumentResult? DocumentImage
    {
        get; set;
    }
    internal OutboxStatus Outbox { get; set; } = null!;
    internal AtomicPartitionPlacementResolution Placement { get; set; } = null!;
    internal bool OriginalAllowed { get; set; } = true;
    internal ProcessingRequest? OutputProcessing
    {
        get; set;
    }
    internal CommitReceipt? OutputReceipt
    {
        get; set;
    }
}
