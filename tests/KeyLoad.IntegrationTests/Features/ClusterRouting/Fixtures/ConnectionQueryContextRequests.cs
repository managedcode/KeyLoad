using KeyLoad.Client;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ConnectionQueryContextRequests
{
    internal const string AddedId = "query-context-row";
    internal const string Path = "bounded-full-scan";
    internal const string FirstJson = """{"cohort":"same-data-epoch","value":3}""";
    internal const string SecondJson = """{"cohort":"same-data-epoch","value":4}""";
    internal static QueryRequest Sql(PartitionRef partition, string? cursor = null)
        => new(partition, "SELECT TOP (1) * FROM [cluster-routing-c1] ORDER BY id", AllowFullScan: true, Cursor: cursor);
    internal static AstQueryRequest Ast(PartitionRef partition, string? cursor = null)
        => new(partition, new(RequestCqrsRf3Protocol.AdminCollection, null, [new("*", "*")], null,
            [new("/@id", false)], 1), AllowFullScan: true, Cursor: cursor);
    internal static AstQueryRequest CSharp(PartitionRef partition, string? cursor = null)
        => KeyLoadQuery.From<ContextDocument>(partition, RequestCqrsRf3Protocol.AdminCollection,
            IntegrationClientOptions.Translation()).OrderBy(row => QueryFunctions.DocumentId(row)).Take(1)
            .ToRequest(true, cursor);
    internal static QueryRow[] Rows(ConnectionQueryContextSeed seed, bool first)
        => first ? [new(AddedId, 1L, seed.AddedJson, false, [])]
            : [new(RequestCqrsRf3Protocol.DocumentId, 1L, RequestCqrsRf3Protocol.DocumentJson, false, [])];
    private sealed record ContextDocument(string Cohort, decimal Value);
}
