using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal static class GlobalBranchSinglePartitionAdapter
{
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
            || index > 0 && GlobalBranchOrder.CompareScores(branch[index - 1], branch[index]) > 0)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidLocalOrder);
        }
    }
}
