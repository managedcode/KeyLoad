using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal sealed class ClusterRestoreMixedRetentionRf3State(QueueLifecyclePublicState queue,
    PrincipalRecord principal, PrincipalRecord inspector, PhysicalShardRecord sourceOwner, PhysicalShardRecord targetOwner)
{
    internal QueueLifecyclePublicState OriginalQueue { get; } = queue;
    internal QueueLifecyclePublicState CurrentQueue { get; } = new(queue.Partition, queue.OperatorKey, queue.DeniedKey, queue.Route);
    internal PrincipalRecord Principal { get; } = principal;
    internal PrincipalRecord Inspector { get; } = inspector;
    internal PhysicalShardRecord CurrentSourceOwner { get; set; } = sourceOwner;
    internal PhysicalShardRecord CurrentTargetOwner { get; set; } = targetOwner;
    internal string Key => OriginalQueue.OperatorKey;
    internal string InspectorKey => OriginalQueue.DeniedKey;
    internal PartitionRef Partition => OriginalQueue.Partition;
    internal PhysicalShardRecord SourceOwner { get; } = sourceOwner;
    internal PhysicalShardRecord TargetOwner { get; } = targetOwner;
    internal QueueLaneRef Source => new(Partition with { PartitionKey = ClusterRestoreMixedRetentionRf3Protocol.SourceKey },
        ClusterRestoreMixedRetentionRf3Protocol.Input);
    internal QueueLaneRef Target => new(Partition, ClusterRestoreMixedRetentionRf3Protocol.Inbox);
    internal EventSourceRef FilteredSource => new(Partition, ClusterRestoreMixedRetentionRf3Protocol.Filtered, EventSourceKind.Topic);
    internal EventSourceRef PurgedSource => new(Partition, ClusterRestoreMixedRetentionRf3Protocol.Purged, EventSourceKind.Topic);
    internal SubscriptionRef FilteredGroup => new(FilteredSource, ClusterRestoreMixedRetentionRf3Protocol.Group);
    internal SubscriptionRef PurgeGroup => new(PurgedSource, ClusterRestoreMixedRetentionRf3Protocol.PurgeGroup);
    internal CommandRequest FilteredPublish { get; set; } = null!;
    internal CommitReceipt FilteredPublishReceipt { get; set; } = null!;
    internal EventSourcePage FilteredPage { get; set; } = null!;
    internal ReceiveSubscriptionRequest FilteredRequest { get; set; } = null!;
    internal ReceiveSubscriptionResult FilteredReceived { get; set; } = null!;
    internal SubscriptionDeliveryCommand FilteredAck { get; set; } = null!;
    internal CommitReceipt FilteredAckReceipt { get; set; } = null!;
    internal SubscriptionInfo FilteredInfo { get; set; } = null!;
    internal CommandRequest PurgedPublish { get; set; } = null!;
    internal CommitReceipt PurgedPublishReceipt { get; set; } = null!;
    internal CommandRequest Purge { get; set; } = null!;
    internal CommitReceipt PurgeReceipt { get; set; } = null!;
    internal EventSourcePage PurgedPage { get; set; } = null!;
    internal SubscriptionInfo PurgeInfo { get; set; } = null!;
    internal ReceiveRequest InputRequest { get; set; } = null!;
    internal ReceiveResult InputReceived { get; set; } = null!;
    internal CommitInboxRequest Inbox { get; set; } = null!;
    internal CommitInboxResult InboxResult { get; set; } = null!;
    internal MessageInspection InputImage { get; set; } = null!;
    internal MessageInspection OutputImage { get; set; } = null!;
    internal DocumentResult DocumentImage { get; set; } = null!;
    internal ImmutableArray<EventRecord> InboxEvents { get; set; }
    internal CommitInboxRequest FreshInbox { get; set; } = null!;
    internal CommitInboxResult FreshInboxResult { get; set; } = null!;
    internal DeliveryCommand FreshAck { get; set; } = null!;
    internal CommitReceipt FreshAckReceipt { get; set; } = null!;
    internal CommandRequest SourceCancel { get; set; } = null!;
    internal CommitReceipt SourceCancelReceipt { get; set; } = null!;
    internal MessageInspection FinalOriginalInput { get; set; } = null!;
    internal CommandRequest FreshEnqueue { get; set; } = null!;
    internal CommitReceipt FreshEnqueueReceipt { get; set; } = null!;
    internal MessageInspection FreshInputImage { get; set; } = null!;
    internal ImmutableArray<EventRecord> FreshInboxEvents { get; set; }
    internal SubscriptionInfo CurrentFilteredInfo { get; set; } = null!;
    internal List<(SubscriptionDeliveryCommand Command, CommitReceipt Receipt)> CurrentSubscriptionAcks { get; } = [];
    internal Dictionary<Guid, byte[]> OriginalOutcomeBytes { get; } = [];
    internal bool Continued { get; set; }
    public override string ToString() => nameof(ClusterRestoreMixedRetentionRf3State);
}
