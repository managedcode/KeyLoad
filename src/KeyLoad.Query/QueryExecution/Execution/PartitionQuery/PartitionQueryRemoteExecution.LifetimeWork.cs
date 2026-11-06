using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Query.QueryExecution;

internal static partial class PartitionQueryRemoteExecution
{
    internal static Task<PartitionQueryPage> ExecuteBoundAsync(QueryEngine engine, PartitionQueryRequest request,
        Func<PartitionQueryLeafPlan, ReadExecutionBudget, CancellationToken, Task<PartitionQueryPreparedLeaf>> prepare,
        Func<CancellationToken, Task> revalidate,
        IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: BCL null validation accepts the binding IResourceLifetimeWorkBinding reference through its object parameter. */;
        return ExecuteAsync(engine, request, prepare, revalidate, token, binding);
    }
}
