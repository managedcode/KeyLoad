using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

/// <summary>Creates a real scoped StreamSet and stable typed append for RF3 caller parity.</summary>
/// <param name="Partition">The unique configured tenant partition.</param>
internal sealed record McpEventStreamScenario(PartitionRef Partition)
{
    /// <summary>Gets the immutable typed events appended by this scenario.</summary>
    internal static ImmutableArray<EventData> ExpectedEvents { get; } =
    [
        new(McpEventStreamTokens.EventIdCreated, McpEventStreamTokens.EventType,
            McpEventStreamTokens.InitialPayload, McpEventStreamTokens.InitialHeaders,
            SchemaVersion: McpEventStreamTokens.EventSchemaVersion, OccurredAt: McpEventStreamTokens.OccurredAt,
            CorrelationId: McpEventStreamTokens.CorrelationId,
            CausationId: McpEventStreamTokens.CausationIdCreated),
        new(McpEventStreamTokens.EventIdPaid, McpEventStreamTokens.EventType,
            McpEventStreamTokens.PaidPayload, McpEventStreamTokens.PaidHeaders,
            SchemaVersion: McpEventStreamTokens.EventSchemaVersion, OccurredAt: McpEventStreamTokens.OccurredAt,
            CorrelationId: McpEventStreamTokens.CorrelationId,
            CausationId: McpEventStreamTokens.CausationIdPaid),
        new(McpEventStreamTokens.EventIdShipped, McpEventStreamTokens.EventType,
            McpEventStreamTokens.ShippedPayload, McpEventStreamTokens.ShippedHeaders,
            SchemaVersion: McpEventStreamTokens.EventSchemaVersion, OccurredAt: McpEventStreamTokens.OccurredAt,
            CorrelationId: McpEventStreamTokens.CorrelationId,
            CausationId: McpEventStreamTokens.CausationIdShipped)
    ];

    /// <summary>Gets the canonical stream identity configured by the scenario.</summary>
    internal StreamRef Stream => new(Partition, McpEventStreamTokens.StreamSet,
        McpEventStreamTokens.StreamId, McpEventStreamTokens.StreamGeneration);

    /// <summary>Creates the resource using the public SDK against the initialized RF3 cluster.</summary>
    /// <param name="fixture">The actual Docker/Aspire RF3 application.</param>
    /// <param name="cancellationToken">The bounded external caller lifetime.</param>
    /// <returns>The configured real stream scenario.</returns>
    internal static async Task<McpEventStreamScenario> CreateAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var partition = new PartitionRef(McpEventStreamTokens.TenantPrefix
            + Guid.NewGuid().ToString(McpEventStreamTokens.GuidFormat), McpEventStreamTokens.Database,
            McpEventStreamTokens.Domain, Guid.NewGuid().ToString(McpEventStreamTokens.GuidFormat));
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var client = new KeyLoadClient(http, fixture.AdminKey);
        var definition = new ResourceDefinition(McpEventStreamTokens.StreamSet, ResourceKind.StreamSet,
            partition.TransactionDomainId);
        await McpCallerAssertions.SdkSuccessAsync(await client.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, definition), cancellationToken));
        return new(partition);
    }

    /// <summary>Creates the canonical append command while preserving its stable command identifier.</summary>
    /// <param name="commandId">The caller-owned retry identity.</param>
    /// <returns>The actual typed public batch command.</returns>
    internal CommandRequest AppendCommand(Guid commandId) => new(commandId, Partition,
        [new AppendEvents(McpEventStreamTokens.StreamSet, McpEventStreamTokens.StreamId, ExpectedEvents,
            ExpectedStreamRevision.NoStream, McpEventStreamTokens.StreamGeneration)]);

    /// <summary>Builds a public bounded stream read at the supplied exclusive revision.</summary>
    /// <param name="afterRevision">The exclusive continuation revision.</param>
    /// <param name="limit">The requested page bound.</param>
    /// <param name="generation">The stream generation to read.</param>
    /// <returns>The canonical SDK/MCP request.</returns>
    internal ReadStreamRequest ReadRequest(long afterRevision, int limit,
        long generation = McpEventStreamTokens.StreamGeneration)
        => new(Stream with { Generation = generation }, afterRevision, limit);
}
