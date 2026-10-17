using KeyLoad.Core;

namespace KeyLoad.Query.Features.ChangeFeeds;

/// <summary>Admits only live deltas selected by the original feed page boundary.</summary>
internal sealed class LiveQueryResultByteAdmission(ReadExecutionBudget budget)
{
    private long retainedBytes;

    internal void Accept(LiveQueryChange change)
    {
        var bytes = budget.MeasureResult(change);
        if (bytes > budget.MaximumResultBytes - retainedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, QueryEngine.ResultLimitExceeded);
        }
        retainedBytes += bytes;
    }
}
