using System.Collections.Immutable;
using System.Text.Json;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlGraphPathTestSupport
{
    internal const string Graph = "path-links";
    internal const string Orders = "path-orders";
    internal const string Projects = "path-projects";
    internal const string StartId = "path-start";
    internal const string MiddleId = "path-middle";
    internal const string TargetId = "path-target";
    internal const string FirstEdge = "path-edge-a";
    internal const string SecondEdge = "path-edge-b";
    internal const string Label = "next";
    internal const string Principal = "root";
    internal const int ContractVersion = 1;
    internal const int PathDepth = 2;
    internal const int MaximumVertices = 10;
    internal const int MaximumEdges = 20;
    internal const string LiteralSql = "SELECT * FROM GRAPH_SHORTEST_PATH('path-links', "
        + "'path-orders', 'path-start', 'path-orders', 'path-target', 2, 10, 20)";
    internal const string ParameterSql = "SELECT * FROM GRAPH_SHORTEST_PATH(@graph, @fromCollection, @fromId, "
        + "@toCollection, @toId, @depth, @vertices, @edges, @label)";

    internal static TestDatabase CreateSeededDatabase()
    {
        var database = new TestDatabase();
        database.Configure(Orders, ResourceKind.Collection);
        database.Configure(Projects, ResourceKind.Collection);
        database.Configure(Graph, ResourceKind.Graph);
        database.Commit(
            new PutDocument(Orders, StartId, "{}"),
            new PutDocument(Projects, MiddleId, "{}"),
            new PutDocument(Orders, TargetId, "{}"));
        var start = Vertex(database, Orders, StartId);
        var middle = Vertex(database, Projects, MiddleId);
        var target = Vertex(database, Orders, TargetId);
        database.Commit(
            new UpsertEdge(Graph, FirstEdge, start, middle, Label),
            new UpsertEdge(Graph, SecondEdge, middle, target, Label));
        return database;
    }

    internal static GraphShortestPathRequest Direct(TestDatabase database, ImmutableArray<string>? labels = null)
        => new(ContractVersion, database.Partition, Graph, Vertex(database, Orders, StartId), Vertex(database, Orders, TargetId),
            PathDepth, MaximumVertices, MaximumEdges, labels);

    internal static SqlGraphPathRequest Request(TestDatabase database, string sql,
        Dictionary<string, JsonElement>? parameters = null, int version = ContractVersion, bool allowFullScan = true,
        string? cursor = null)
        => new(version, new(database.Partition, sql, parameters, allowFullScan, cursor));

    internal static Dictionary<string, JsonElement> Parameters(params (string Name, object Value)[] values)
        => values.ToDictionary(value => value.Name, value => JsonSerializer.SerializeToElement(value.Value),
            StringComparer.Ordinal);

    private static EntityRef Vertex(TestDatabase database, string collection, string id)
        => new(database.Partition, collection, id);
}
