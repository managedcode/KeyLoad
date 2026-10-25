using KeyLoad.Core;

namespace KeyLoad.Query;

/// <summary>Composes the configured read output ceiling without replacing the operation's original budget.</summary>
internal static class QueryResultBudgetPolicy
{
    private const int UnconfiguredResultBytes = int.MaxValue;

    internal static int Resolve(QueryExecutionOptions execution)
        => execution.MaximumResultBytes ?? UnconfiguredResultBytes;

    internal static void Constrain(ReadExecutionBudget budget, QueryExecutionOptions execution)
    {
        if (execution.MaximumResultBytes is { } maximum)
        {
            budget.ConstrainResultBytes(maximum);
        }
    }
}
