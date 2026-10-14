using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed class EventProjectionLineageR41State(PartitionRef partition,
 AtomicPartitionPlacementResolution placement, McpPersistedIdentity worker, McpPersistedIdentity reader)
{
    internal PartitionRef Partition { get; } = partition;
    internal AtomicPartitionPlacementResolution Placement { get; } = placement;
    internal McpPersistedIdentity Worker { get; } = worker;
    internal McpPersistedIdentity Reader { get; } = reader;
    internal StreamRef Stream => new(Partition, EventProjectionLineageR41Protocol.Events, EventProjectionLineageR41Protocol.Stream, 1L);
    internal List<(CommandRequest Request, CommitReceipt Receipt)> Successes { get; } = [];
    internal CommandRequest Projection { get; set; } = null!;
    internal CommitReceipt Original { get; set; } = null!;
    internal CommandRequest FreshProjection { get; set; } = null!;
    internal EventRecord OriginalEvent { get; set; } = null!;
    internal OutboxStatus Outbox { get; set; } = null!;
    internal bool Deleted
    {
        get; set;
    }
    internal bool Replaced
    {
        get; set;
    }
}
