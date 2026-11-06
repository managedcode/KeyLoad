using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Query.Search;

internal static class SearchLifetimeWorkBudget
{
    internal static ReadExecutionBudget Create(
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: BCL null validation accepts the binding IResourceLifetimeWorkBinding reference through its object parameter. */;
        var budget = new ReadExecutionBudget(token);
        budget.AttachResourceLifetimeBinding(binding);
        budget.Check();
        return budget;
    }
}
