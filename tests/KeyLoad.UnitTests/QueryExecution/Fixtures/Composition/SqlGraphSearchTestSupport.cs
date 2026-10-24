using System.Text.Json;
using KeyLoad.UnitTests.Search;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlGraphSearchTestSupport
{
    internal const string Collection = GraphSearchTestSupport.Documents;
    internal const string Graph = GraphSearchTestSupport.Graph;
    internal const string Projects = GraphSearchTestSupport.Projects;
    internal const string Root = GraphSearchTestSupport.Root;
    internal const string TextField = GraphSearchTestSupport.TextField;
    internal const string VectorField = "/embedding";
    internal const string VectorModel = "graph-sql-model";
    internal const string VectorVersion = "graphsqlversion";
    internal const string VectorSpaceId = "graph-sql-space";
    internal const int Depth = 4;
    internal const int Vertices = 20;
    internal const int Edges = 40;
    internal const int Limit = 10;
    internal const int Fusion = 60;

    internal static Dictionary<string, JsonElement> Parameters(params (string Name, object Value)[] values)
        => values.ToDictionary(value => value.Name, value => JsonSerializer.SerializeToElement(value.Value), StringComparer.Ordinal);

    internal static SqlGraphSearchRequest Request(PartitionRef partition, string sql,
        Dictionary<string, JsonElement>? parameters = null, bool fullScan = true, string? cursor = null)
        => new(1, new(partition, sql, parameters, fullScan, cursor));

    internal static string RetrieverSql(string graph = Graph, string collection = Collection,
        string? weight = null, string? allowedIds = null)
    {
        var graphWeight = weight ?? "1";
        var allowed = allowedIds is null ? "NULL" : $"GRAPH_IDS({allowedIds})";
        return $"SELECT * FROM GRAPH_SEARCH('{collection}', NULL, NULL, NULL, "
            + $"GRAPH_RETRIEVER('{graph}', GRAPH_SEEDS(ENTITY('{Projects}','{Root}')), "
            + $"{Depth}, {Vertices}, {Edges}, NULL, {graphWeight}), NULL, {allowed}, {Limit}, {Fusion})";
    }

    internal static VectorSpace Space()
        => new(VectorSpaceId, 2, DistanceMetric.Cosine, VectorModel, VectorVersion);
}
