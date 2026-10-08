namespace KeyLoad.Core.Features.Search;

internal sealed class AnnSeedWork(ReadExecutionBudget budget, long maximum)
{
    private const long SingleWorkUnit = 1;

    private const string WorkExceeded = "The ANN seed work exceeds its budget.";
    private long units;

    internal long Units => Volatile.Read(ref units);

    internal void Charge(long amount = SingleWorkUnit)
    {
        const int AmountValidationBoundary = 0;

        budget.Check();
        var previous = Volatile.Read(ref units);
        if (amount < AmountValidationBoundary || amount > maximum - previous)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, WorkExceeded);
        }
        Volatile.Write(ref units, previous + amount);
    }

    internal void Check() => budget.Check();
}
