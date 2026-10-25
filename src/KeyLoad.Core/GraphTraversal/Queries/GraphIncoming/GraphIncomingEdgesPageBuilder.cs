using System.Collections.Immutable;

namespace KeyLoad.Core.GraphTraversal;

internal sealed class GraphIncomingEdgesPageBuilder
{
    private const string TooManyIncoming = "The graph incoming-edge result exceeds its limit.";
    private const int Version = 1;
    private readonly DatabaseEngine database;
    private readonly PrincipalRecord principal;
    private readonly int limit;
    private readonly ReadExecutionBudget budget;
    private readonly long cutPosition;
    private readonly ImmutableArray<GraphIncomingEdgeRow>.Builder rows =
        ImmutableArray.CreateBuilder<GraphIncomingEdgeRow>();

    internal GraphIncomingEdgesPageBuilder(DatabaseEngine database, PrincipalRecord principal,
        int limit, ReadExecutionBudget budget, long cutPosition)
    {
        this.database = database;
        this.principal = principal;
        this.limit = limit;
        this.budget = budget;
        this.cutPosition = cutPosition;
    }

    internal void Add(EdgeRecord edge, long deliveredRevision, ResourceDefinition graph)
    {
        if (rows.Count >= limit)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, TooManyIncoming);
        }
        var projected = edge with
        {
            AttributesJson = database.Authorization.Project(principal, graph.FieldPolicies,
                edge.AttributesJson, out _)
        };
        rows.Add(new GraphIncomingEdgeRow(projected, deliveredRevision));
    }

    internal GraphIncomingEdgesPage Build()
    {
        budget.Check();
        var comparer = new GraphIncomingEdgesOrderComparer(budget);
        try
        {
            rows.Sort(comparer);
        }
        catch (InvalidOperationException exception)
        {
            comparer.RethrowOwnedBudgetFailure(exception);
            throw;
        }
        var result = new GraphIncomingEdgesPage(Version, rows.ToImmutable(), cutPosition,
            GraphCrossPartitionProtocol.EventualProjection);
        budget.CheckResult(result);
        return result;
    }
}
