namespace KeyLoad.Core.Features.Search;

internal sealed class AnnSeedWork(ReadExecutionBudget budget, long maximum)
{
    private const string WorkExceeded = "The ANN seed work exceeds its budget.";
    private long units;

    internal long Units => units;

    internal void Charge(long amount = 1)
    {
        budget.Check();
        if (amount < 0 || amount > maximum - units)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, WorkExceeded);
        }
        units += amount;
    }

    internal void Check() => budget.Check();
}
