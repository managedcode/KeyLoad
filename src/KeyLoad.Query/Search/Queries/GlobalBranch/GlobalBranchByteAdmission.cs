using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal sealed class GlobalBranchByteAdmission(int maximumBytes, ReadExecutionBudget budget)
{
    private const int NoRetainedBytes = 0;

    private const string ResourceExceeded = "The global branch merge exceeds its configured bounds.";
    private long retainedBytes;

    internal void Accept(long bytes)
    {
        budget.Check();
        if (bytes < NoRetainedBytes || bytes > maximumBytes - retainedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceExceeded);
        }
        budget.ChargeBytes(bytes);
        retainedBytes = checked(retainedBytes + bytes);
    }
}
