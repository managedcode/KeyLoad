using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Only actual public source results; no caller roles or synthesized authority.</summary>
internal sealed class ClusterRestoreRf3EventingState(PartitionRef partition)
{
    internal PartitionRef Partition { get; } = partition;
    internal EventSourceRef Source { get; } = new(partition, ClusterRestoreRf3EventingProtocol.Topic, EventSourceKind.Topic);
    internal SubscriptionRef Group => new(Source, ClusterRestoreRf3EventingProtocol.Group);
    internal SubscriptionRef Other => new(Source, ClusterRestoreRf3EventingProtocol.OtherGroup);
    internal QueueLaneRef Lane { get; } = new(partition, ClusterRestoreRf3EventingProtocol.Queue);
    internal EventSourcePage Page { get; set; } = null!;
    internal ReceiveSubscriptionResult Received { get; set; } = null!;
    internal ReceiveResult QueueClaim { get; set; } = null!;
    internal SubscriptionProcessingRequest ProcessRequest { get; set; } = null!;
    internal SubscriptionProcessingResult Processed { get; set; } = null!;
    internal MessageInspection Message { get; set; } = null!;
    internal OutboxStatus Outbox { get; set; } = null!;
    internal ImmutableArray<SourceEventRecord> FinalEvents { get; set; }
    internal OutboxStatus FinalOutbox { get; set; } = null!;
    internal MessageInspection HealthyMessage { get; set; } = null!;
    internal bool Resumed { get; set; }
    internal long CurrentCommitPosition { get; set; }
    public override string ToString() => nameof(ClusterRestoreRf3EventingState);
}
