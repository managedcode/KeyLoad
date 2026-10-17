using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal static class GlobalBranchSinglePartitionAdapter
{
    private const int EmptyElementCount = 0;
    private const int OneBranch = 1;
    private const int EqualOrder = 0;

    private const string InvalidLocalOrder = "The local search branch is not ordered by the global branch contract.";

    internal static SearchScore[] Prepare(SearchScore[] branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        return branch;
    }

    internal static void ValidateAt(SearchScore[] branch, int index, ReadExecutionBudget budget)
    {
        budget.Check();
        if (!double.IsFinite(branch[index].Score)
            || index > EmptyElementCount && GlobalBranchOrder.CompareScores(branch[index - OneBranch], branch[index]) > EqualOrder)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidLocalOrder);
        }
    }
}
