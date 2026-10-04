using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.Search;

internal static class ThreeWayHybridTestSupport
{
    internal const string Documents = "three-way-documents";
    internal const string Projects = "three-way-projects";
    internal const string Graph = "three-way-graph";
    internal const string ExpansionGraph = "three-way-expansion-graph";
    internal const string TextField = "/body";
    internal const string VectorField = "/embedding";
    internal const string TextGrant = "three-way.text.use";
    internal const string VectorGrant = "three-way.vector.use";
    internal const string LabelGrant = "three-way.graph.label.use";
    internal const string Reader = "three-way-reader";
    internal const string TextOnlyReader = "three-way-text-only";
    internal const string LabelDeniedReader = "three-way-label-denied";
    internal const string A = "a";
    internal const string B = "b";
    internal const string C = "c";
    internal const string D = "d";
    internal const string E = "e";
    internal const string RetrieveRoot = "retrieve-root";
    internal const string ScopeRoot = "scope-root";
    internal const string MiddleOne = "middle-one";
    internal const string MiddleTwo = "middle-two";
    internal const string MiddleThree = "middle-three";
    internal const string MiddleFour = "middle-four";
    internal const string Context = "context";
    internal const int FusionConstant = 10;
    internal const double TextWeight = 2;
    internal const double VectorWeight = 1;
    internal const double GraphWeight = 13d / 132d;
    internal const double Tolerance = 0.000000000001;

    internal static VectorSpace Space { get; } = new("three-way-space", 2, DistanceMetric.Cosine,
        "three-way-model", "1");

    internal static void Configure(TestDatabase database)
    {
        database.Configure(Documents, ResourceKind.Collection, fields:
        [
            new(TextField, "text", RawUseGrant: TextGrant),
            new(VectorField, "vector", RawUseGrant: VectorGrant)
        ]);
        database.Configure(Projects, ResourceKind.Collection);
        database.Configure(Graph, ResourceKind.Graph, fields: [new("/label", "graph-label", RawUseGrant: LabelGrant)]);
        database.Configure(ExpansionGraph, ResourceKind.Graph);
    }

    internal static void Seed(TestDatabase database)
    {
        database.Commit(
            new PutDocument(Documents, A, "{\"body\":\"signal signal signal filler\"}"),
            new PutDocument(Documents, B, "{\"body\":\"signal signal filler filler\"}"),
            new PutDocument(Documents, C, "{\"body\":\"signal filler filler filler\"}"),
            new PutDocument(Documents, D, "{\"body\":\"filler filler filler filler\"}"),
            new PutDocument(Documents, E, "{\"body\":\"filler filler filler filler\"}"),
            new PutDocument(Projects, RetrieveRoot, "{}"),
            new PutDocument(Projects, ScopeRoot, "{}"),
            new PutDocument(Projects, MiddleOne, "{}"),
            new PutDocument(Projects, MiddleTwo, "{}"),
            new PutDocument(Projects, MiddleThree, "{}"),
            new PutDocument(Projects, MiddleFour, "{}"),
            new PutDocument(Projects, Context, "{\"body\":\"context\"}"),
            new PutVector(Documents, A, VectorField, [0.8f, 0.6f], Space, 1),
            new PutVector(Documents, B, VectorField, [1, 0], Space, 1),
            new PutVector(Documents, C, VectorField, [0, 1], Space, 1),
            new PutVector(Documents, D, VectorField, [-1, 0], Space, 1));
        var retrieve = Vertex(database, Projects, RetrieveRoot);
        var scope = Vertex(database, Projects, ScopeRoot);
        var first = Vertex(database, Documents, A);
        var second = Vertex(database, Documents, B);
        var third = Vertex(database, Documents, C);
        var fourth = Vertex(database, Documents, D);
        var middleOne = Vertex(database, Projects, MiddleOne);
        var middleTwo = Vertex(database, Projects, MiddleTwo);
        var middleThree = Vertex(database, Projects, MiddleThree);
        var middleFour = Vertex(database, Projects, MiddleFour);
        var context = Vertex(database, Projects, Context);
        database.Commit(
            new UpsertEdge(Graph, "scope-a", scope, first, "related"),
            new UpsertEdge(Graph, "scope-b", scope, second, "related"),
            new UpsertEdge(Graph, "scope-c", scope, third, "related"),
            new UpsertEdge(Graph, "retrieve-d", retrieve, fourth, "related"),
            new UpsertEdge(Graph, "retrieve-mid-one", retrieve, middleOne, "related"),
            new UpsertEdge(Graph, "mid-one-c", middleOne, third, "related"),
            new UpsertEdge(Graph, "retrieve-mid-two", retrieve, middleTwo, "related"),
            new UpsertEdge(Graph, "mid-two-c", middleTwo, third, "related"),
            new UpsertEdge(Graph, "retrieve-mid-three", retrieve, middleThree, "related"),
            new UpsertEdge(Graph, "mid-three-four", middleThree, middleFour, "related"),
            new UpsertEdge(Graph, "mid-four-b", middleFour, second, "related"),
            new UpsertEdge(Graph, "c-cycle", third, middleOne, "related"),
            new UpsertEdge(Graph, "b-cycle", second, retrieve, "related"),
            new UpsertEdge(ExpansionGraph, "a-context", first, context, "related"),
            new UpsertEdge(ExpansionGraph, "b-context", second, context, "related"));
    }

    internal static void PersistReader(TestDatabase database, bool vectorGrant, string? principalId = null)
    {
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new PrincipalRecord(
            principalId ?? (vectorGrant ? Reader : TextOnlyReader), "tenant",
            [
                new("database", Documents, Capability.Query | Capability.DocumentsRead | Capability.VectorSearch),
                new("database", Projects, Capability.DocumentsRead),
                new("database", Graph, Capability.GraphRead),
                new("database", ExpansionGraph, Capability.GraphRead)
            ], vectorGrant ? ImmutableArray.Create(TextGrant, VectorGrant) : ImmutableArray.Create(TextGrant))));
    }

    internal static GraphSearchRequest Request(PartitionRef partition, ImmutableArray<string>? allowedIds = null,
        bool scoped = false, bool expansion = false, bool allZero = false, bool labeled = false)
    {
        var search = new SearchRequest(partition, Documents, TextField, "signal", VectorField, [1, 0], Space,
            Limit: 10, TextWeight: allZero ? 0 : TextWeight, VectorWeight: allZero ? 0 : VectorWeight,
            FusionConstant: FusionConstant, AllowedIds: allowedIds);
        var retrieveWalk = Walk(partition, RetrieveRoot, 4) with
        { Labels = labeled ? ImmutableArray.Create("related") : null };
        return new(1, search,
            Scope: scoped ? new(Walk(partition, ScopeRoot, 2)) : null,
            Retriever: new(retrieveWalk, allZero ? 0 : GraphWeight),
            Expansion: expansion ? new(ExpansionGraph, MaxDepth: 1, MaxVertices: 20, MaxEdges: 40) : null);
    }

    internal static EntityRef Vertex(TestDatabase database, string collection, string id)
        => new(database.Partition, collection, id);

    internal static GraphWalkSpec Walk(PartitionRef partition, string root, int depth)
        => new(Graph, [new(partition, Projects, root)], depth, MaxVertices: 30, MaxEdges: 60);
}
