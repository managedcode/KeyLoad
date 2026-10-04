using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Search;

internal static class GraphSearchTestSupport
{
    internal const string Documents = "graph-search-documents";
    internal const string Projects = "graph-search-projects";
    internal const string Graph = "graph-search-links";
    internal const string TextField = "/text";
    internal const string Root = "root-node";
    internal const string Middle = "middle-node";
    internal const string FirstHit = "a-hit";
    internal const string SecondHit = "b-hit";
    internal const string Label = "related";
    internal const string Reader = "graph-search-reader";
    internal const string Tenant = "tenant";
    internal const string DatabaseScope = "database";
    internal const string LabelUseGrant = "graph.label.use";

    internal static void Configure(TestDatabase database, bool protectLabels = false)
    {
        database.Configure(Documents, ResourceKind.Collection);
        database.Configure(Projects, ResourceKind.Collection);
        database.Configure(Graph, ResourceKind.Graph, fields: protectLabels ? [new("label", "graph-label")] : null);
    }

    internal static EntityRef Vertex(TestDatabase database, string collection, string id)
        => new(database.Partition, collection, id);

    internal static void AddPath(TestDatabase database)
    {
        database.Commit(
            new PutDocument(Projects, Root, "{\"name\":\"root\"}"),
            new PutDocument(Projects, Middle, "{\"name\":\"middle\"}"),
            new PutDocument(Documents, FirstHit, "{\"text\":\"needle needle\"}"),
            new PutDocument(Documents, SecondHit, "{\"text\":\"needle\"}"));
        var root = Vertex(database, Projects, Root);
        var middle = Vertex(database, Projects, Middle);
        var first = Vertex(database, Documents, FirstHit);
        var second = Vertex(database, Documents, SecondHit);
        database.Commit(
            new UpsertEdge(Graph, "edge-root-middle", root, middle, Label),
            new UpsertEdge(Graph, "edge-root-first", root, first, Label),
            new UpsertEdge(Graph, "edge-middle-first", middle, first, Label),
            new UpsertEdge(Graph, "edge-middle-second", middle, second, Label),
            new UpsertEdge(Graph, "edge-first-middle", first, middle, Label),
            new UpsertEdge(Graph, "edge-first-root", first, root, Label));
    }

    internal static void PersistReader(TestDatabase database, Capability extra = Capability.None,
        string[]? fieldGrants = null, bool restrictRows = false, string? ownerId = null)
    {
        var grants = new[]
        {
            new ScopeGrant(DatabaseScope, Documents, Capability.Query | Capability.DocumentsRead),
            new ScopeGrant(DatabaseScope, Projects, Capability.DocumentsRead),
            new ScopeGrant(DatabaseScope, Graph, Capability.GraphRead | extra)
        };
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new PrincipalRecord(
            Reader, Tenant, [.. grants], [.. fieldGrants ?? []]) { RestrictRows = restrictRows, OwnerId = ownerId }));
    }

    internal static GraphWalkSpec Walk(TestDatabase database, params EntityRef[] seeds)
        => new(Graph, [.. seeds], MaxDepth: 4, MaxVertices: 20, MaxEdges: 40);
}
