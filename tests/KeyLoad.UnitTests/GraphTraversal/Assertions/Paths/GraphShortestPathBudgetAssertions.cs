using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

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

    internal static long MetadataCharge(int jsonBytes) => checked(checked(2L * jsonBytes) + 128);

    internal static async Task AssertQueryBudgetExceeded(TestDatabase database,
        GraphShortestPathRequest request, int maxQueryBytes)
    {
        var position = database.Store.Position;
        var limits = new DatabaseLimits { MaxQueryBytes = maxQueryBytes };
        var engine = new DatabaseEngine(database.Store, database.Database.Authorization, UnitExecutionOptions.DatabaseLimits(limits), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.RuntimeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.ShortestPath(
            GraphShortestPathTestSupport.RootPrincipal, request));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    internal static async Task AssertReadBudgetExceeded(TestDatabase database,
        GraphShortestPathRequest request, long maxReadBytes)
    {
        var position = database.Store.Position;
        var limits = new DatabaseLimits { MaxQueryReadBytes = maxReadBytes };
        var engine = new DatabaseEngine(database.Store, database.Database.Authorization, UnitExecutionOptions.DatabaseLimits(limits), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.RuntimeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(limits));
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.ShortestPath(
            GraphShortestPathTestSupport.RootPrincipal, request, budget));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    internal static async Task AssertResponseBudgetExceeded(TestDatabase database,
        GraphShortestPathRequest request, long maxReadBytes, int maxBatchBytes)
    {
        var position = database.Store.Position;
        var limits = new DatabaseLimits { MaxQueryReadBytes = maxReadBytes, MaxBatchBytes = maxBatchBytes };
        var engine = new DatabaseEngine(database.Store, database.Database.Authorization, UnitExecutionOptions.DatabaseLimits(limits), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.RuntimeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(limits));
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.ShortestPath(
            GraphShortestPathTestSupport.RootPrincipal, request, budget));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
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
