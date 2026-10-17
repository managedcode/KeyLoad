using KeyLoad.Core;

namespace KeyLoad.Query;

/// <summary>Composes the configured read output ceiling without replacing the operation's original budget.</summary>
internal static class QueryResultBudgetPolicy
{
    internal static int Resolve(DatabaseLimits limits, QueryExecutionOptions execution)
        => Math.Min(limits.MaxBatchBytes, execution.MaximumResultBytes ?? limits.MaxBatchBytes);

    internal static void Constrain(ReadExecutionBudget budget, QueryExecutionOptions execution)
    {
        if (execution.MaximumResultBytes is { } maximum)
        {
            budget.ConstrainResultBytes(maximum);
        }
    }
}
