using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.QueryExecution;

internal static partial class DistributedSearchCoordinator
{
    internal static async Task<DistributedSearchPage> ExecuteAsync(DatabaseEngine database,
        DistributedSearchRequest request, IOptions<QueryExecutionOptions> options,
        Func<DistributedSearchPhaseWork, ReadExecutionBudget, CancellationToken, Task<DistributedSearchPreparedLeaf>> prepare,
        Func<CancellationToken, Task> sourceBarrier, TimeProvider clock, DateTimeOffset originalExpiry,
        CancellationToken original, Func<CancellationToken, Task>? statisticsCaptured = null,
        IResourceLifetimeWorkBinding? binding = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(sourceBarrier);
        ArgumentNullException.ThrowIfNull(clock);
        var budget = new ReadExecutionBudget(clock, original);
        if (binding is not null)
        {
            budget.AttachResourceLifetimeBinding(binding);
        }
        budget.ConstrainLifetime(originalExpiry);
        using var admission = database.AdmitQuery(original);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(original);
        var partitions = DistributedSearchValidation.Require(request, options, budget);
        return await new DistributedSearchOperation(request, partitions, options, budget,
            prepare, sourceBarrier, execution, statisticsCaptured).RunAsync().ConfigureAwait(true);
    }
}
