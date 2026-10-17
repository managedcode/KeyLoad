using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Query.Features.Search;

internal static class SearchLifetimeWorkBudget
{
    internal static ReadExecutionBudget Create(DatabaseEngine database, QueryExecutionOptions execution,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, token);
        budget.AttachResourceLifetimeBinding(binding);
        QueryResultBudgetPolicy.Constrain(budget, execution);
        budget.Check();
        return budget;
    }

    internal static async Task<T> RunNativeAsync<T>(DatabaseEngine database, QueryExecutionOptions execution,
        IResourceLifetimeWorkBinding binding, Func<ReadExecutionBudget, T> read, CancellationToken token)
    {
        var budget = Create(database, execution, binding, token);
        using var reservation = database.AdmitQuery(token);
        return await Task.Run(() => read(budget), token).ConfigureAwait(false);
    }
}
