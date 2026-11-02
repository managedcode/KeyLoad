using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.QueryExecution;

internal static partial class DistributedSearchCoordinator
{
    internal static async Task<DistributedSearchPage> ExecuteAsync(DatabaseEngine database,
        DistributedSearchRequest request, IOptions<QueryExecutionOptions> options,
        Func<DistributedSearchPhaseWork, ReadExecutionBudget, CancellationToken, Task<DistributedSearchPreparedLeaf>> prepare,
        Func<CancellationToken, Task> sourceBarrier, TimeProvider clock,
        CancellationToken original, Func<CancellationToken, Task>? statisticsCaptured = null,
        IResourceLifetimeWorkBinding? binding = null)
    {
        ArgumentNullException.ThrowIfNull(database) /* KLD0041: BCL null validation accepts the database DatabaseEngine reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: BCL null validation accepts the request DistributedSearchRequest reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(options) /* KLD0041: BCL null validation accepts the options IOptions reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(prepare) /* KLD0041: BCL null validation accepts the prepare Func reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(sourceBarrier) /* KLD0041: BCL null validation accepts the sourceBarrier Func reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(clock) /* KLD0041: BCL null validation accepts the clock TimeProvider reference through its object parameter. */;
        var budget = new ReadExecutionBudget(clock, original);
        if (binding is not null)
        {
            budget.AttachResourceLifetimeBinding(binding);
        }
        using var admission = database.AdmitQuery(original);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(original);
        var partitions = DistributedSearchValidation.Require(request, options, budget);
        return await new DistributedSearchOperation(request, partitions, options, budget,
            prepare, sourceBarrier, execution, statisticsCaptured).RunAsync().ConfigureAwait(true);
    }
}
