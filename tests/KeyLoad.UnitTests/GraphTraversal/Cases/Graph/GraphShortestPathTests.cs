using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphShortestPathTests
{
    private const string Orders = "orders";
    private const string Projects = "projects";
    private const string StartId = "start";
    private const string FirstBranchId = "same-id";
    private const string SecondBranchId = "same-id";
    private const string JoinId = "join";
    private const string IsolatedId = "isolated";
    private const string SelfEdge = "m-self";
    private const string FirstEdge = "a-first-branch";
    private const string SecondEdge = "z-second-branch";
    private const string FirstJoinEdge = "a-first-join";
    private const string SecondJoinEdge = "b-second-join";
    private const string DirectJoinEdge = "zz-shortcut";
    private const string CycleEdge = "c-cycle";
    private const string Label = GraphShortestPathTestSupport.Walk;

    [Test]
    public async Task AcGraph006ShortestPathMatchesIndependentBfsForTiesCyclesAndFullEntityRefs()
    {
        using var database = GraphShortestPathTestSupport.CreateDatabase(null, Orders, Projects);
        var start = GraphShortestPathTestSupport.Ref(Orders, StartId);
        var first = GraphShortestPathTestSupport.Ref(Orders, FirstBranchId);
        var second = GraphShortestPathTestSupport.Ref(Projects, SecondBranchId);
        var join = GraphShortestPathTestSupport.Ref(Projects, JoinId);
        var isolated = GraphShortestPathTestSupport.Ref(Orders, IsolatedId);
        GraphShortestPathTestSupport.PersistVertices(database, [start, first, second, join, isolated]);
        GraphShortestPathReferenceEdge[] edges =
        [
            new(CycleEdge, first, start, Label),
            new(SecondJoinEdge, second, join, Label),
            new(DirectJoinEdge, start, join, "shortcut"),
            new(SelfEdge, start, start, Label),
            new(SecondEdge, start, second, Label),
            new(FirstJoinEdge, first, join, Label),
            new(FirstEdge, start, first, Label)
        ];
        GraphShortestPathTestSupport.PersistEdges(database, edges);
        var token = TestContext.Current!.Execution.CancellationToken;

        var primaryResults = await GraphShortestPathAssertions.AssertPrimaryPathsAsync(
            database, edges, start, first, join, FirstEdge, FirstJoinEdge, DirectJoinEdge, token);
        var boundedResults = await GraphShortestPathAssertions.AssertBoundedPathsAsync(
            database, edges, start, join, isolated, token);
        await GraphShortestPathAssertions.AssertSameCutAsync(database, [.. primaryResults, .. boundedResults]);
    }

    [Test]
    public async Task EmptyLabelsMeansUnrestrictedAndUsesTheShortestAvailableEdge()
    {
        using var database = GraphShortestPathTestSupport.CreateDatabase(null, Orders);
        var start = GraphShortestPathTestSupport.Ref(Orders, "label-start");
        var target = GraphShortestPathTestSupport.Ref(Orders, "label-target");
        GraphShortestPathTestSupport.PersistVertices(database, [start, target]);
        GraphShortestPathReferenceEdge[] edges =
        [
            new("a-skip", start, target, GraphShortestPathTestSupport.Skip),
            new("z-walk", start, target, GraphShortestPathTestSupport.Walk)
        ];
        GraphShortestPathTestSupport.PersistEdges(database, edges);
        var request = GraphShortestPathTestSupport.Request(database, start, target,
            labels: ImmutableArray<string>.Empty);

        var actual = database.Database.ShortestPath(GraphShortestPathTestSupport.RootPrincipal, request,
            cancellationToken: TestContext.Current!.Execution.CancellationToken);

        var expected = GraphShortestPathReferenceBfs.Find(edges, start, target, 16);
        await GraphShortestPathAssertions.MatchAsync(database, actual, expected);
        await Assert.That(actual.Edges.Select(edge => edge.Id).SequenceEqual(["a-skip"])).IsTrue();
    }
}
