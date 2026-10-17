using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Execution;

internal sealed class PartitionMoveCleanupReadBudget(long maximumRetainedBytes, long maximumExaminedBytes)
{
    private long retained;
    internal long ExaminedBytes
    {
        get;
        private set;
    }

    internal void Examine(long bytes)
    {
        Require(bytes, maximumExaminedBytes, ExaminedBytes);
        ExaminedBytes = checked(ExaminedBytes + bytes);
    }

    internal void Retain(long bytes)
    {
        Require(bytes, maximumRetainedBytes, retained);
        retained = checked(retained + bytes);
    }

    private static void Require(long bytes, long maximum, long used)
    {
        if (bytes < PartitionMoveProtocol.EmptyCount || bytes > maximum - used)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity);
        }
    }
}
