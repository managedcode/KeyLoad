using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.QueryExecution;

internal static partial class DistributedSearchCoordinator
{
    internal static Task<DistributedSearchPage> ExecuteBoundAsync(DatabaseEngine database,
        DistributedSearchRequest request, IOptions<QueryExecutionOptions> options,
        Func<DistributedSearchPhaseWork, ReadExecutionBudget, CancellationToken, Task<DistributedSearchPreparedLeaf>> prepare,
        Func<CancellationToken, Task> sourceBarrier, TimeProvider clock,
        Func<CancellationToken, Task>? statisticsCaptured, IResourceLifetimeWorkBinding binding,
        CancellationToken original)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return ExecuteAsync(database, request, options, prepare, sourceBarrier, clock,
            original, statisticsCaptured, binding);
    }
}
