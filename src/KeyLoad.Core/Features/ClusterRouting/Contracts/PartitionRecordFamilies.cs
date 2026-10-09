using System.Collections.Immutable;

namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Closed ordinal inventory of current partition-key constructor families.</summary>
internal static class PartitionRecordFamilies
{
    internal const string Adjacency = "adjacency";
    internal const string AggregateSnapshot = "aggregate-snapshot-v1";
    internal const string BlobHead = "blob-head-v1";
    internal const string BlobPart = "blob-part-v1";
    internal const string BlobPartMetadata = "blob-partmeta-v1";
    internal const string BlobState = "blob-state-v1";
    internal const string DeadLetter = "dead-letter";
    internal const string Document = "document";
    internal const string DocumentEpoch = "document-epoch";
    internal const string Edge = "edge";
    internal const string Event = "event";
    internal const string EventFeed = "event-feed";
    internal const string EventIdentity = "event-id";
    internal const string EventSequence = "event-sequence";
    internal const string GraphCrossPartitionCapacity = "graph-cross-partition-capacity";
    internal const string GraphCrossPartitionIntent = "graph-cross-partition-intent";
    internal const string GraphCrossReverseAdjacency = "graph-cross-reverse-adjacency";
    internal const string GraphEdgeOwnerVersion = "graph-edge-owner-version";
    internal const string Inbox = "inbox";
    internal const string Index = "index";
    internal const string Lease = "lease";
    internal const string MessageBody = "message-body";
    internal const string MessageMetadata = "message-meta";
    internal const string Outbox = "outbox";
    internal const string OutboxHead = "outbox-head";
    internal const string OutcomeLocator = "outcome-locator-v1";
    internal const string OutcomeV2 = "outcome-v2";
    internal const string OutcomeLocatorV2 = "outcome-locator-v2";
    internal const string ProjectionConsumer = "projection-consumer";
    internal const string ProjectionReceipt = "projection-receipt";
    internal const string QueueCounters = "queue-counters";
    internal const string QueueTransferIntent = "queue-transfer-intent";
    internal const string QueueTransferSourceCapacity = "queue-transfer-source-capacity";
    internal const string QueueTransferTargetCapacity = "queue-transfer-target-capacity";
    internal const string QueueTransferTargetReceipt = "queue-transfer-target-receipt";
    internal const string Ready = "ready";
    internal const string RecurringSagaCapacity = "recurring-saga-capacity";
    internal const string RecurringSchedule = "recurring-schedule";
    internal const string SagaState = "saga-state";
    internal const string Sample = "sample";
    internal const string SampleChunkBlock = "sample-chunk-block";
    internal const string SampleChunkCorrection = "sample-chunk-correction";
    internal const string SampleChunkManifest = "sample-chunk-manifest";
    internal const string SampleChunkWindow = "sample-chunk-window";
    internal const string SampleIdentity = "sample-id";
    internal const string SampleRetention = "sample-retention-v1";
    internal const string SampleRollup = "sample-rollup-v1";
    internal const string SampleSequence = "sample-sequence";
    internal const string Scheduled = "scheduled";
    internal const string StreamHead = "stream-head";
    internal const string Subscription = "subscription";
    internal const string SubscriptionCompletion = "subscription-completion";
    internal const string SubscriptionInbox = "subscription-inbox";
    internal const string SubscriptionWindow = "subscription-window";
    internal const string TopicEvent = "topic-event";
    internal const string TopicEventIdentity = "topic-event-id";
    internal const string TopicHead = "topic-head";
    internal const string Unique = "unique";
    internal const string Vector = "vector";
    internal const string VectorProjectionEffect = "vector-projection-effect";
    internal const string VectorProjectionLineage = "vector-projection-lineage";
    internal const string VisibilityEpoch = "visibility-epoch";

    internal static ImmutableArray<string> All { get; } =
    [
        Adjacency,
        AggregateSnapshot,
        BlobHead,
        BlobPart,
        BlobPartMetadata,
        BlobState,
        DeadLetter,
        Document,
        DocumentEpoch,
        Edge,
        Event,
        EventFeed,
        EventIdentity,
        EventSequence,
        GraphCrossPartitionCapacity,
        GraphCrossPartitionIntent,
        GraphCrossReverseAdjacency,
        GraphEdgeOwnerVersion,
        Inbox,
        Index,
        Lease,
        MessageBody,
        MessageMetadata,
        Outbox,
        OutboxHead,
        OutcomeLocator,
        OutcomeLocatorV2,
        OutcomeV2,
        ProjectionConsumer,
        ProjectionReceipt,
        QueueCounters,
        QueueTransferIntent,
        QueueTransferSourceCapacity,
        QueueTransferTargetCapacity,
        QueueTransferTargetReceipt,
        Ready,
        RecurringSagaCapacity,
        RecurringSchedule,
        SagaState,
        Sample,
        SampleChunkBlock,
        SampleChunkCorrection,
        SampleChunkManifest,
        SampleChunkWindow,
        SampleIdentity,
        SampleRetention,
        SampleRollup,
        SampleSequence,
        Scheduled,
        StreamHead,
        Subscription,
        SubscriptionCompletion,
        SubscriptionInbox,
        SubscriptionWindow,
        TopicEvent,
        TopicEventIdentity,
        TopicHead,
        Unique,
        Vector,
        VectorProjectionEffect,
        VectorProjectionLineage,
        VisibilityEpoch
    ];
}
