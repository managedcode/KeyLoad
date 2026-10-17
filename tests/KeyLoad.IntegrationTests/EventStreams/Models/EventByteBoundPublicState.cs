using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal sealed class EventByteBoundPublicState(McpEventStreamScenario scenario,
    McpPersistedIdentity reader, AtomicPartitionPlacementResolution placement,
    CommandRequest command, CommitReceipt receipt, StreamPage original, OutboxStatus outbox)
{
    internal McpEventStreamScenario Scenario { get; } = scenario;
    internal McpPersistedIdentity Reader { get; } = reader;
    internal AtomicPartitionPlacementResolution Placement { get; } = placement;
    internal CommandRequest Command { get; } = command;
    internal CommitReceipt Receipt { get; } = receipt;
    internal StreamPage Original { get; } = original;
    internal OutboxStatus Outbox { get; } = outbox;
    internal List<(StreamReadDirection Direction, StreamPage Page)> Captures { get; } = [];
}
