using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedSearchGrants
{
    internal const int PhaseCount = 4;
    private const int FirstGrant = 0;
    private const int MinimumResultBytes = 1;
    private const long NoBytes = 0;
    private const string InsufficientGrant = "The distributed search phase grants exceed the original operation budget.";

    internal static ReadExecutionBudgetReadGrant[] Reserve(int leafCount, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leafCount);
        budget.Check();
        var count = checked(leafCount * PhaseCount);
        budget.ChargeBytes(checked(PartitionQueryRetention.ArrayDescriptorBytes
            + (long)count * PartitionQueryRetention.LeafDescriptorBytes));
        var availableBytes = budget.RemainingReadGrantBytes - budget.MaximumResultBytes;
        if (availableBytes < NoBytes || budget.MaximumResultBytes / count < MinimumResultBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, InsufficientGrant); }
        var bytes = availableBytes / count;
        var records = budget.RemainingReadGrantRecords / count;
        var grants = new ReadExecutionBudgetReadGrant[count];
        for (var index = FirstGrant; index < grants.Length; index++)
        { grants[index] = budget.CreateReadGrant(bytes, records); }
        return grants;
    }

    internal static int ResultBytes(int leafCount, ReadExecutionBudget budget)
        => budget.MaximumResultBytes / checked(leafCount * PhaseCount);

    internal static int Index(DistributedSearchPhase phase, int leaf, int leafCount)
        => checked((int)phase * leafCount + leaf);
}
