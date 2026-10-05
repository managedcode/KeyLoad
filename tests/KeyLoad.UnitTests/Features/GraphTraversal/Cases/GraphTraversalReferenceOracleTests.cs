using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphTraversalReferenceOracleTests
{
    private const string OrdersCollection = "orders";
    private const string ProjectsCollection = "projects";
    private const string GraphName = "links";
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const string Tenant = "tenant";
    private const string DatabaseScope = "database";
    private const string Walk = "walk";
    private const string Skip = "skip";
    private const string EmptyDocument = "{}";
    private const string StartId = "m";
    private const string LeftId = "a";
    private const string RightId = "b";
    private const string DirectId = "z";
    private const string JoinId = LeftId;
    private const string HiddenId = "h";
    private const string HiddenTargetId = HiddenId;
    private const string FilteredId = RightId;
    private const string EdgeStartRight = "z-start-right";
    private const string EdgeStartLeft = "a-start-left";
    private const string EdgeStartDirect = "m-start-direct";
    private const string EdgeStartHidden = "h-start-hidden";
    private const string EdgeStartFiltered = "f-start-filtered";
    private const string EdgeLeftJoin = "c-left-join";
    private const string EdgeRightJoin = "b-right-join";
    private const string EdgeLeftDirect = "d-left-direct";
    private const string EdgeDirectCycle = "x-direct-cycle";
    private const string EdgeHiddenDownstream = "u-hidden-downstream";
    private const string EdgeFilteredDownstream = "v-filtered-downstream";
    private const int RequestedVertices = 20;
    private const int RequestedEdges = 20;
    private const int StartDepth = 0;
    private const int FirstHopDepth = 1;
    private const int BoundedDepth = 2;
    private const int DeeperCycleDepth = 4;
    private const int OneLess = 1;

    private static readonly GraphTraversalReferenceVertex Start = new(OrdersCollection, StartId);
    private static readonly GraphTraversalReferenceVertex Left = new(ProjectsCollection, LeftId);
    private static readonly GraphTraversalReferenceVertex Right = new(OrdersCollection, RightId);
    private static readonly GraphTraversalReferenceVertex Direct = new(ProjectsCollection, DirectId);
    private static readonly GraphTraversalReferenceVertex Join = new(OrdersCollection, JoinId);
    private static readonly GraphTraversalReferenceVertex Hidden = new(ProjectsCollection, HiddenId);
    private static readonly GraphTraversalReferenceVertex HiddenTarget = new(OrdersCollection, HiddenTargetId);
    private static readonly GraphTraversalReferenceVertex Filtered = new(ProjectsCollection, FilteredId);
    private static readonly int[] DepthCases = [StartDepth, FirstHopDepth, BoundedDepth, DeeperCycleDepth];
    private static readonly GraphTraversalReferenceVertex[] ExpectedBoundedVertices = [Join, Right, Start, Left, Direct];
    private static readonly GraphTraversalReferenceEdge[] Edges =
    [
        new(EdgeStartRight, Start, Right, Walk),
        new(EdgeStartLeft, Start, Left, Walk),
        new(EdgeStartDirect, Start, Direct, Walk),
        new(EdgeStartHidden, Start, Hidden, Walk),
        new(EdgeStartFiltered, Start, Filtered, Skip),
        new(EdgeLeftJoin, Left, Join, Walk),
        new(EdgeRightJoin, Right, Join, Walk),
        new(EdgeLeftDirect, Left, Direct, Walk),
        new(EdgeDirectCycle, Direct, Start, Walk),
        new(EdgeHiddenDownstream, Hidden, HiddenTarget, Walk),
        new(EdgeFilteredDownstream, Filtered, HiddenTarget, Walk)
    ];

    private static readonly HashSet<GraphTraversalReferenceVertex> AliceVisibleVertices =
    [
        Start,
        Left,
        Right,
        Direct,
        Join,
        Filtered
    ];

    [Test]
    public async Task TraversalMatchesIndependentReferenceAtEachDepthAndKeepsOrdinalEntityOrder()
    {
        using var database = CreateSeededDatabase();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        foreach (var depth in DepthCases)
        {
            var expected = GraphTraversalReferenceBfsOracle.Read(Edges, AliceVisibleVertices, Start,
                depth, RequestedVertices, RequestedEdges, [Walk]);
            var actual = database.Database.Traverse(Alice, database.Partition, GraphName, Vertex(database, Start),
                depth, RequestedVertices, RequestedEdges, [Walk], cancellationToken);

            await Assert.That(expected.BudgetExceeded).IsFalse();
            await Assert.That(actual.Vertices)
                .IsEquivalentTo(ExpandVertices(database, expected), CollectionOrdering.Matching);
            await Assert.That(actual.Edges.Select(EdgeIdentity))
                .IsEquivalentTo(expected.Edges.Select(edge => EdgeIdentity(database, edge)), CollectionOrdering.Matching);
            await Assert.That(actual.Vertices.Contains(Vertex(database, Join)))
                .IsEqualTo(depth >= BoundedDepth);
        }

        var bounded = GraphTraversalReferenceBfsOracle.Read(Edges, AliceVisibleVertices, Start,
            BoundedDepth, RequestedVertices, RequestedEdges, [Walk]);
        await Assert.That(bounded.ShortestDepths[Direct]).IsEqualTo(FirstHopDepth);
        await Assert.That(bounded.ShortestDepths[Join]).IsEqualTo(BoundedDepth);
        await Assert.That(ExpandVertices(database, bounded))
            .IsEquivalentTo(ExpectedBoundedVertices.Select(vertex => Vertex(database, vertex)), CollectionOrdering.Matching);
        await Assert.That(bounded.Edges.Select(edge => edge.Id)).IsEquivalentTo(
            bounded.Edges.Select(edge => edge.Id).OrderBy(edgeId => edgeId, StringComparer.Ordinal), CollectionOrdering.Matching);
    }

    [Test]
    public async Task ExactDistinctVertexAndExaminedEdgeCapsSucceedAndOneLessRejects()
    {
        using var database = CreateSeededDatabase();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var expected = GraphTraversalReferenceBfsOracle.Read(Edges, AliceVisibleVertices, Start,
            BoundedDepth, RequestedVertices, RequestedEdges, [Walk]);
        var requiredVertices = expected.ShortestDepths.Count;
        var requiredEdges = expected.ExaminedEdges;

        var exact = database.Database.Traverse(Alice, database.Partition, GraphName, Vertex(database, Start),
            BoundedDepth, requiredVertices, requiredEdges, [Walk], cancellationToken);
        await Assert.That(exact.Vertices)
            .IsEquivalentTo(ExpandVertices(database, expected), CollectionOrdering.Matching);
        await Assert.That(exact.Edges.Select(EdgeIdentity))
            .IsEquivalentTo(expected.Edges.Select(edge => EdgeIdentity(database, edge)), CollectionOrdering.Matching);

        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => database.Database.Traverse(
            Alice, database.Partition, GraphName, Vertex(database, Start), BoundedDepth,
            requiredVertices - OneLess, requiredEdges, [Walk], cancellationToken)).Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => database.Database.Traverse(
            Alice, database.Partition, GraphName, Vertex(database, Start), BoundedDepth,
            requiredVertices, requiredEdges - OneLess, [Walk], cancellationToken)).Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
    }

    private static TestDatabase CreateSeededDatabase()
    {
        var database = new TestDatabase();
        try
        {
            database.Configure(OrdersCollection, ResourceKind.Collection);
            database.Configure(ProjectsCollection, ResourceKind.Collection);
            database.Configure(GraphName, ResourceKind.Graph);
            database.Commit(
                Document(Start, Alice),
                Document(Left, Alice),
                Document(Right, Alice),
                Document(Direct, Alice),
                Document(Join, Alice),
                Document(Hidden, Bob),
                Document(HiddenTarget, Bob),
                Document(Filtered, Alice));
            database.Commit(Edges.Select(edge => new UpsertEdge(
                GraphName, edge.Id, Vertex(database, edge.From), Vertex(database, edge.To), edge.Label)).ToArray());
            database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new PrincipalRecord(Alice, Tenant,
                [new(DatabaseScope, OrdersCollection, Capability.DocumentsRead),
                    new(DatabaseScope, ProjectsCollection, Capability.DocumentsRead),
                    new(DatabaseScope, GraphName, Capability.GraphRead)], [])
            { OwnerId = Alice, RestrictRows = true })).Get<PrincipalRecord>();
            return database;
        }
        catch (Exception primary)
        {
            try
            {
                database.Dispose();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(primary, cleanup);
            }
            throw;
        }
    }

    private static PutDocument Document(GraphTraversalReferenceVertex vertex, string owner)
        => new(vertex.Collection, vertex.Id, EmptyDocument, Access: new(owner));

    private static EntityRef Vertex(TestDatabase database, GraphTraversalReferenceVertex vertex)
        => new(database.Partition, vertex.Collection, vertex.Id);

    private static EntityRef[] ExpandVertices(TestDatabase database, GraphTraversalReferenceResult result)
        => result.Vertices.Select(vertex => Vertex(database, vertex)).ToArray();

    private static GraphEdgeIdentity EdgeIdentity(TestDatabase database, GraphTraversalReferenceEdge edge)
        => new(edge.Id, Vertex(database, edge.From), Vertex(database, edge.To));

    private static GraphEdgeIdentity EdgeIdentity(EdgeRecord edge) => new(edge.Id, edge.From, edge.To);

    private readonly record struct GraphEdgeIdentity(string Id, EntityRef From, EntityRef To);
}
