using System.Collections.Immutable;
using KeyLoad.Clustering;

namespace KeyLoad.Server;

/// <summary>Official MCP and exact T-SQL EXEC bindings to the same canonical analytics reads.</summary>
internal static class AnalyticsStoreMcpCatalog
{
    internal static ImmutableArray<McpOperationDescriptor> Entries
    {
        get;
    } =
    [
        McpOperationFactory.Read<ReadAnalyticsEventsRequest, AnalyticsEventsPage>(AnalyticsStoreProtocol.ReadTool,
            AnalyticsStoreProtocol.ReadRoute, GrainReadKind.AnalyticsEvents),
        McpOperationFactory.Read<AggregateAnalyticsRequest, AnalyticsAggregateResult>(AnalyticsStoreProtocol.AggregateTool,
            AnalyticsStoreProtocol.AggregateRoute, GrainReadKind.AnalyticsAggregate)
    ];
}
