using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.BackupRestore;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal sealed class UnacknowledgedArchiveState(ClusterRestoreMixedRetentionRf3State original)
{
    internal ClusterRestoreMixedRetentionRf3State Original
    {
        get;
    } = original;
    internal EventSourceRef Source => new(Original.Partition, UnacknowledgedArchiveProtocol.Topic, EventSourceKind.Topic);
    internal SubscriptionRef Group => new(Source, UnacknowledgedArchiveProtocol.Group);
    internal SubscriptionDefinition Definition => new(Original.Principal.Id);
    internal AtomicPartitionPlacementResolution Placement
    {
        get; set;
    } = null!;
    internal OutboxStatus Outbox
    {
        get; set;
    } = null!;
    internal ImmutableArray<SourceEventRecord> Rows
    {
        get; set;
    } = [];
    internal bool HasGroup
    {
        get; set;
    }
    internal long Generation
    {
        get; set;
    } = UnacknowledgedArchiveProtocol.InitialGeneration;
    internal long Checkpoint
    {
        get; set;
    }
    internal long Issued
    {
        get; set;
    }
    internal long Tail
    {
        get; set;
    } = UnacknowledgedArchiveProtocol.BeforeFirst;
    internal long First
    {
        get; set;
    } = UnacknowledgedArchiveProtocol.FirstPosition;
    internal bool Paused
    {
        get; set;
    }
    internal CommandRequest Publish
    {
        get; set;
    } = null!;
    internal CommitReceipt Published
    {
        get; set;
    } = null!;
    internal ReceiveSubscriptionRequest SourceReceive
    {
        get; set;
    } = null!;
    internal ReceiveSubscriptionResult SourceReceived
    {
        get; set;
    } = null!;
    internal ReceiveSubscriptionRequest Receive
    {
        get; set;
    } = null!;
    internal ReceiveSubscriptionResult Received
    {
        get; set;
    } = null!;
    internal List<(string Tool, object Request, object Result)> Replays
    {
        get;
    } = [];
    internal List<CommandRequest> SourceFailures
    {
        get;
    } = [];
    internal List<CommandRequest> CurrentFailures
    {
        get;
    } = [];
    internal bool Restored
    {
        get; set;
    }
    internal bool Settled
    {
        get; set;
    }
}
