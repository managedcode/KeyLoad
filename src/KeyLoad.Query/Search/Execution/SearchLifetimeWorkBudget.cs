using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Query.Search;

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
}
