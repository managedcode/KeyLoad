using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.ResourceExecution.Execution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.QueryExecution;

internal static partial class DistributedSearchCoordinator
{
    internal static Task<DistributedSearchPage> ExecuteBoundAsync(DatabaseEngine database,
        DistributedSearchRequest request, IOptions<QueryExecutionOptions> options,
        Func<DistributedSearchPhaseWork, ReadExecutionBudgetReadGrant, ReadExecutionBudget, CancellationToken,
            Task<DistributedSearchPreparedLeaf>> prepare,
        Func<CancellationToken, Task> sourceBarrier, TimeProvider clock, DateTimeOffset originalExpiry,
        Func<CancellationToken, Task>? statisticsCaptured, IResourceLifetimeWorkBinding binding,
        CancellationToken original)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return ExecuteAsync(database, request, options, prepare, sourceBarrier, clock, originalExpiry,
            original, statisticsCaptured, binding);
    }
}
