using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.EventStreams;

internal sealed class EventSchemaCursorPublicState(McpEventStreamScenario scenario,
    McpPersistedIdentity reader, ResourceDefinition initial, CommandRequest command, CommitReceipt receipt, StreamPage original, OutboxStatus outbox)
{
    internal McpEventStreamScenario Scenario { get; } = scenario;
    internal McpPersistedIdentity Reader { get; } = reader;
    internal ResourceDefinition Initial { get; } = initial;
    internal ResourceDefinition Definition { get; set; } = initial;
    internal CommandRequest Command { get; } = command;
    internal CommitReceipt Receipt { get; } = receipt;
    internal StreamPage Original { get; } = original;
    internal OutboxStatus Outbox { get; } = outbox;
    internal List<(long Schema, StreamReadDirection Direction, StreamPage Page)> Captures { get; } = [];
    internal List<(Guid Id, ConfigureResourceRequest Request, ResourceDefinition Result)> Configurations { get; } = [];
}
