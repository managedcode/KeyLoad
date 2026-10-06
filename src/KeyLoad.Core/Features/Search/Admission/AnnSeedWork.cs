namespace KeyLoad.Core.Features.Search;

internal sealed class AnnSeedWork(ReadExecutionBudget budget, long maximum)
{
    private const long SingleWorkUnit = 1;

    private const string WorkExceeded = "The ANN seed work exceeds its budget.";
    private long units;

    internal long Units => units;

    internal void Charge(long amount = SingleWorkUnit)
    {
        const int AmountValidationBoundary = 0;

        budget.Check();
        if (amount < AmountValidationBoundary || amount > maximum - units)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, WorkExceeded);
        }
        units += amount;
    }

    internal void Check() => budget.Check();
}
