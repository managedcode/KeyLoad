using KeyLoad.Core;

namespace KeyLoad.Query.Search;

/// <summary>Waits for a physical projection owner before a new read acquires work.</summary>
internal static class TextProjectionAdmission
{
    internal static KeyLoadException Busy(string detail, Task availability)
    {
        var failure = Errors.Fail(ErrorCode.BudgetExceeded, detail);
        failure.Data[typeof(TextProjectionAdmission)] = availability;
        return failure;
    }

    internal static async Task<T> ExecuteAsync<T>(Func<T> execute, ReadExecutionBudget budget,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            budget.Check();
            try
            {
                return execute();
            }
            catch (KeyLoadException failure) when (failure.Code == ErrorCode.BudgetExceeded
                && failure.Data[typeof(TextProjectionAdmission)] is Task)
            {
                var availability = (Task)failure.Data[typeof(TextProjectionAdmission)]!;
                await availability.WaitAsync(cancellationToken);
            }
        }
    }
}
