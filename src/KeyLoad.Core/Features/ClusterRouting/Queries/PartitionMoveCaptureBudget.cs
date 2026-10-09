using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Queries;

internal sealed class PartitionMoveCaptureBudget(ReadExecutionBudget work, int maximumRecords)
{
    private int examined;

    internal void Observe(long bytes)
    {
        work.Check();
        if (examined >= maximumRecords)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        examined = checked(examined + PartitionMoveProtocol.SequenceStep);
        work.ChargeNativeReadRecord(bytes);
        work.Check();
    }
}
