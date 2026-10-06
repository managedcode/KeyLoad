using System.Runtime.ExceptionServices;

namespace KeyLoad.Core.Features.GraphTraversal;

internal sealed class GraphSearchReachabilityOrderComparer(ReadExecutionBudget budget)
    : IComparer<GraphSearchReachability>
{
    private ExceptionDispatchInfo? budgetFailure;

    public int Compare(GraphSearchReachability left, GraphSearchReachability right)
    {
        const int EqualOrderIdentity = 0;

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
        var depth = left.ShortestHops.CompareTo(right.ShortestHops);
        if (depth != EqualOrderIdentity)
        {
            return depth;
        }
        var partition = ComparePartition(left.Reference.Partition, right.Reference.Partition);
        if (partition != EqualOrderIdentity)
        {
            return partition;
        }
        var collection = StringComparer.Ordinal.Compare(left.Reference.Collection, right.Reference.Collection);
        return collection != EqualOrderIdentity ? collection : StringComparer.Ordinal.Compare(left.Reference.Id, right.Reference.Id);
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
