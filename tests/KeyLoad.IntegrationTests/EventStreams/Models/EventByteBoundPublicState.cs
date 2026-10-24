using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.EventStreams;

internal sealed class EventByteBoundPublicState(McpEventStreamScenario scenario,
    McpPersistedIdentity reader, CommandRequest command, CommitReceipt receipt, StreamPage original, OutboxStatus outbox)
{
    internal McpEventStreamScenario Scenario { get; } = scenario;
    internal McpPersistedIdentity Reader { get; } = reader;
    internal CommandRequest Command { get; } = command;
    internal CommitReceipt Receipt { get; } = receipt;
    internal StreamPage Original { get; } = original;
    internal OutboxStatus Outbox { get; } = outbox;
    internal List<(StreamReadDirection Direction, StreamPage Page)> Captures { get; } = [];
}
