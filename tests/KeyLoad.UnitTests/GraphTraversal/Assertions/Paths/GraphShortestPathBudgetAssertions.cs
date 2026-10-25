using System.Collections.Immutable;

namespace KeyLoad.UnitTests.GraphTraversal;

internal static class GraphShortestPathBudgetAssertions
{
    internal static async Task AssertBudgetExceeded(TestDatabase database,
        GraphShortestPathReferenceVertex source, GraphShortestPathReferenceVertex target,
        int maxDepth, int maxVertices, int maxEdges, CancellationToken token)
    {
        var request = GraphShortestPathTestSupport.Request(database, source, target, maxDepth, maxVertices, maxEdges);
        await AssertCode(database, request, ErrorCode.BudgetExceeded, token);
    }

    internal static async Task AssertCode(TestDatabase database, GraphShortestPathRequest request,
        ErrorCode expectedCode, CancellationToken token)
    {
        var position = database.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.ShortestPath(
            GraphShortestPathTestSupport.RootPrincipal, request, cancellationToken: token));
        await Assert.That(failure.Code).IsEqualTo(expectedCode);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    internal static async Task RequireShapeRefusalsAsync(TestDatabase database,
        GraphShortestPathRequest request, CancellationToken token)
    {
        await AssertCode(database, request with
        {
            Version = 2
        }, ErrorCode.Validation, token);
        var defaultLabels = (ImmutableArray<string>?)default(ImmutableArray<string>);
        await AssertCode(database, request with
        {
            Labels = defaultLabels
        }, ErrorCode.Validation, token);
        await AssertCode(database, request with
        {
            Labels = [GraphShortestPathTestSupport.Walk, GraphShortestPathTestSupport.Walk]
        },
            ErrorCode.Validation, token);
        await AssertCode(database, request with
        {
            Graph = string.Empty
        }, ErrorCode.Validation, token);
        await AssertCode(database, request with
        {
            From = request.From with
            {
                Id = string.Empty
            }
        }, ErrorCode.Validation, token);
        await AssertCode(database, request with
        {
            Labels = ImmutableArray.Create(string.Empty)
        }, ErrorCode.Validation, token);
        await AssertCode(database, request with
        {
            MaxDepth = 17
        }, ErrorCode.BudgetExceeded, token);
        await AssertCode(database, request with
        {
            MaxVertices = 0
        }, ErrorCode.BudgetExceeded, token);
        await AssertCode(database, request with
        {
            MaxVertices = 10_001
        }, ErrorCode.BudgetExceeded, token);
        await AssertCode(database, request with
        {
            MaxEdges = 0
        }, ErrorCode.BudgetExceeded, token);
        await AssertCode(database, request with
        {
            MaxEdges = 50_001
        }, ErrorCode.BudgetExceeded, token);
        await AssertCode(database, request with
        {
            Labels = ImmutableArray.CreateRange(Enumerable.Range(0, 65).Select(index => $"label-{index}"))
        }, ErrorCode.Validation, token);
    }
}
