using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphShortestPathTestSupport
{
    internal const string Graph = "shortest-path-graph";
    internal const string Tenant = "tenant";
    internal const string DatabaseScope = "database";
    internal const string RootPrincipal = "root";
    internal const string Alice = "path-alice";
    internal const string Bob = "path-bob";
    internal const string Walk = "walk";
    internal const string Skip = "skip";
    internal const string FieldUseGrant = "graph.label.use";
    internal const string EmptyJson = "{}";

    internal static TestDatabase CreateDatabase(SensitiveFieldPolicy[]? graphFields, params string[] collections)
    {
        var database = new TestDatabase();
        foreach (var collection in collections.Distinct(StringComparer.Ordinal))
        {
            database.Configure(collection, ResourceKind.Collection);
        }
        database.Configure(Graph, ResourceKind.Graph, fields: graphFields);
        return database;
    }

    internal static EntityRef Vertex(TestDatabase database, string collection, string id)
        => new(database.Partition, collection, id);

    internal static void PersistVertices(TestDatabase database,
        IEnumerable<GraphShortestPathReferenceVertex> vertices, string? owner = null)
    {
        var documents = vertices.Distinct().Select(vertex => new PutDocument(vertex.Collection, vertex.Id,
            EmptyJson, Access: owner is null ? null : new RowAccess(owner))).ToArray();
        if (documents.Length > 0)
        {
            database.Commit(documents);
        }
    }

    internal static void PersistEdges(TestDatabase database,
        IEnumerable<GraphShortestPathReferenceEdge> edges)
    {
        var mutations = edges.Select(edge => new UpsertEdge(Graph, edge.Id,
            Vertex(database, edge.From.Collection, edge.From.Id),
            Vertex(database, edge.To.Collection, edge.To.Id), edge.Label, edge.AttributesJson)).ToArray();
        if (mutations.Length > 0)
        {
            database.Commit(mutations);
        }
    }

    internal static PrincipalRecord PersistReader(TestDatabase database, IEnumerable<string> collections,
        string[]? fieldGrants = null, string? owner = null, bool restrictRows = false)
    {
        var grants = collections.Select(collection => new ScopeGrant(DatabaseScope, collection,
            Capability.DocumentsRead)).Append(new(DatabaseScope, Graph, Capability.GraphRead)).ToImmutableArray();
        var previous = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(Alice)));
        var principal = new PrincipalRecord(Alice, Tenant, grants, [.. fieldGrants ?? []])
        {
            OwnerId = owner,
            RestrictRows = restrictRows,
            PolicyEpoch = previous is null ? 1 : checked(previous.PolicyEpoch + 1)
        };
        return database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
    }

    internal static GraphShortestPathReferenceVertex Ref(string collection, string id) => new(collection, id);

    internal static GraphShortestPathRequest Request(TestDatabase database,
        GraphShortestPathReferenceVertex from, GraphShortestPathReferenceVertex to,
        int maxDepth = 16, int maxVertices = 1_000, int maxEdges = 5_000,
        ImmutableArray<string>? labels = null)
        => new(1, database.Partition, Graph,
            Vertex(database, from.Collection, from.Id), Vertex(database, to.Collection, to.Id),
            maxDepth, maxVertices, maxEdges, labels);
}
