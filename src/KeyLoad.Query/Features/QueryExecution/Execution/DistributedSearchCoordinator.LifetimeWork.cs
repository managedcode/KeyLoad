using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.QueryExecution;

internal static partial class DistributedSearchCoordinator
{
    internal static Task<DistributedSearchPageV1> ExecuteBoundAsync(DatabaseEngine database,
        DistributedSearchRequestV1 request, IOptions<QueryExecutionOptions> options,
        Func<DistributedSearchPhaseWork, ReadExecutionBudgetReadGrant, ReadExecutionBudget, DistributedSearchPreparedLeaf> prepare,
        Func<CancellationToken, Task> sourceBarrier, TimeProvider clock, DateTimeOffset originalExpiry,
        CancellationToken original, Func<CancellationToken, Task>? statisticsCaptured,
        IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return ExecuteAsync(database, request, options, prepare, sourceBarrier, clock, originalExpiry,
            original, statisticsCaptured, binding);
    }
}
