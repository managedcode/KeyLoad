using System.Collections.Immutable;

namespace KeyLoad.Core.Features.GraphTraversal;

internal sealed class GraphIncomingEdgesPageBuilder
{
    private const string TooManyIncoming = "The graph incoming-edge result exceeds its limit.";
    private const string ResultBytesExceeded = "The graph incoming-edge result exceeds its byte budget.";
    private const int Version = 1;
    private const int ArrayBracketsBytes = 2;
    private const int CommaBytes = 1;
    private readonly DatabaseEngine database;
    private readonly PrincipalRecord principal;
    private readonly int limit;
    private readonly ReadExecutionBudget budget;
    private readonly long cutPosition;
    private readonly ImmutableArray<GraphIncomingEdgeRowV1>.Builder rows =
        ImmutableArray.CreateBuilder<GraphIncomingEdgeRowV1>();
    private long retainedBytes;

    internal GraphIncomingEdgesPageBuilder(DatabaseEngine database, PrincipalRecord principal,
        int limit, ReadExecutionBudget budget, long cutPosition)
    {
        this.database = database;
        this.principal = principal;
        this.limit = limit;
        this.budget = budget;
        this.cutPosition = cutPosition;
        retainedBytes = budget.MeasureResult(EmptyPage());
    }

    internal void Add(EdgeRecord edge, long deliveredRevision, ResourceDefinition graph)
    {
        const int EmptyRowCount = 0;
        const int NoCommaBytes = 0;

        if (rows.Count >= limit)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, TooManyIncoming);
        }
        var projected = edge with
        {
            AttributesJson = database.Authorization.Project(principal, graph.FieldPolicies,
                edge.AttributesJson, out _)
        };
        var row = new GraphIncomingEdgeRowV1(projected, deliveredRevision);
        var rowBytes = budget.MeasureResult(row);
        var addition = checked(rowBytes + (rows.Count > EmptyRowCount ? CommaBytes : NoCommaBytes));
        var adjusted = checked(retainedBytes - ArrayBracketsBytes);
        if (addition > database.Limits.MaxBatchBytes - adjusted)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResultBytesExceeded);
        }
        retainedBytes = checked(adjusted + addition + ArrayBracketsBytes);
        rows.Add(row);
    }

    internal GraphIncomingEdgesPageV1 Build()
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
        var result = new GraphIncomingEdgesPageV1(Version, rows.ToImmutable(), cutPosition,
            GraphCrossPartitionProtocol.EventualProjection);
        budget.CheckResult(result);
        return result;
    }

    private GraphIncomingEdgesPageV1 EmptyPage()
        => new(Version, [], cutPosition, GraphCrossPartitionProtocol.EventualProjection);
}
