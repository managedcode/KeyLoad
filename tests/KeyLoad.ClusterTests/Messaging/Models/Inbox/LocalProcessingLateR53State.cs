namespace KeyLoad.ClusterTests.Messaging;

internal sealed class LocalProcessingLateR53State(QueueLaneRef lane, MessagingClusterIdentity identity)
{
    internal QueueLaneRef Lane { get; } = lane;
    internal MessagingClusterIdentity Identity { get; } = identity;
    internal QueueLaneRef Output => new(Lane.Partition, InboxProcessingClusterProtocol.Output);
    internal EntityRef Document => new(Lane.Partition, InboxProcessingClusterProtocol.Collection, LocalProcessingLateR53Protocol.Document);
    internal EntityRefLifetime CollectionLifetime { get; set; } = null!;
    internal EntityRef StoredDocument => Document with { Lifetime = CollectionLifetime };
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
