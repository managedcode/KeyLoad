using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.ResourceExecution.Execution;

namespace KeyLoad.Query.QueryExecution;

internal static partial class PartitionQueryRemoteExecution
{
    internal static Task<PartitionQueryPage> ExecuteBoundAsync(QueryEngine engine, PartitionQueryRequest request,
        Func<PartitionQueryLeafPlan, ReadExecutionBudgetReadGrant, ReadExecutionBudget, CancellationToken,
            Task<PartitionQueryPreparedLeaf>> prepare,
        Func<CancellationToken, Task> revalidate, TimeProvider clock, DateTimeOffset originalExpiry,
        IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return ExecuteAsync(engine, request, prepare, revalidate, clock, originalExpiry, token, binding);
    }
}
