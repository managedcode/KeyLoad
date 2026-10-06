using System.Runtime.ExceptionServices;

namespace KeyLoad.Core.Features.GraphTraversal;

internal sealed class GraphIncomingEdgesOrderComparer(ReadExecutionBudget budget)
    : IComparer<GraphIncomingEdgeRowV1>
{
    private ExceptionDispatchInfo? budgetFailure;

    public int Compare(GraphIncomingEdgeRowV1? left, GraphIncomingEdgeRowV1? right)
    {
        const int EqualOrderIdentity = 0;

        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        try
        {
            budget.Check();
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken == budget.Cancellation)
        {
            budgetFailure = ExceptionDispatchInfo.Capture(exception);
            throw;
        }
        catch (KeyLoadException exception) when (exception.Code == ErrorCode.BudgetExceeded)
        {
            budgetFailure = ExceptionDispatchInfo.Capture(exception);
            throw;
        }
        var partition = ComparePartition(left.Edge.From.Partition, right.Edge.From.Partition);
        if (partition != EqualOrderIdentity)
        {
            return partition;
        }
        var collection = StringComparer.Ordinal.Compare(left.Edge.From.Collection, right.Edge.From.Collection);
        if (collection != EqualOrderIdentity)
        {
            return collection;
        }
        var vertex = StringComparer.Ordinal.Compare(left.Edge.From.Id, right.Edge.From.Id);
        return vertex != EqualOrderIdentity ? vertex : StringComparer.Ordinal.Compare(left.Edge.Id, right.Edge.Id);
    }

    internal void RethrowOwnedBudgetFailure(InvalidOperationException wrapper)
    {
        if (budgetFailure is { } failure && ReferenceEquals(wrapper.InnerException, failure.SourceException))
        {
            failure.Throw();
        }
    }

    private static int ComparePartition(PartitionRef left, PartitionRef right)
    {
        const int EqualOrderIdentity = 0;

        var tenant = StringComparer.Ordinal.Compare(left.TenantId, right.TenantId);
        if (tenant != EqualOrderIdentity)
        {
            return tenant;
        }
        var database = StringComparer.Ordinal.Compare(left.DatabaseId, right.DatabaseId);
        if (database != EqualOrderIdentity)
        {
            return database;
        }
        var domain = StringComparer.Ordinal.Compare(left.TransactionDomainId, right.TransactionDomainId);
        return domain != EqualOrderIdentity ? domain : StringComparer.Ordinal.Compare(left.PartitionKey, right.PartitionKey);
    }
}
