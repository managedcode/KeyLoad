using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Query.Search;

internal static class SearchLifetimeWorkBudget
{
    internal static ReadExecutionBudget Create(DatabaseEngine database,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var budget = new ReadExecutionBudget(database.EvaluationClock, token);
        budget.AttachResourceLifetimeBinding(binding);
        budget.Check();
        return budget;
    }
}
