using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Query.Features.QueryExecution;

internal static partial class PartitionQueryRemoteExecution
{
    internal static Task<PartitionQueryPageV1> ExecuteBoundAsync(QueryEngine engine, PartitionQueryRequestV1 request,
        Func<PartitionQueryLeafPlanV1, ReadExecutionBudgetReadGrant, ReadExecutionBudget, CancellationToken,
            Task<PartitionQueryPreparedLeaf>> prepare,
        Func<CancellationToken, Task> revalidate, TimeProvider clock, DateTimeOffset originalExpiry,
        IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return ExecuteAsync(engine, request, prepare, revalidate, clock, originalExpiry, token, binding);
    }
}
