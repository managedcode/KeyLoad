using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Search;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlGraphSearchTestSupport
{
    internal const string Collection = GraphSearchTestSupport.Documents;
    internal const string Graph = GraphSearchTestSupport.Graph;
    internal const string Projects = GraphSearchTestSupport.Projects;
    internal const string Root = GraphSearchTestSupport.Root;
    internal const string TextField = GraphSearchTestSupport.TextField;
    internal const string VectorField = "/embedding";
    internal const string VectorModel = "graph-sql-model";
    internal const string VectorVersion = "1";
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

    internal static string RetrieverSql(string graph = Graph, string collection = Collection)
        => $"SEARCH FROM \"{collection}\" RETRIEVE GRAPH \"{graph}\" SEEDS ((\"{Projects}\",'{Root}')) "
            + $"DEPTH {Depth} VERTICES {Vertices} EDGES {Edges} LIMIT {Limit} FUSION {Fusion}";

    internal static VectorSpace Space()
        => new(VectorSpaceId, 2, DistanceMetric.Cosine, VectorModel, VectorVersion);
}
