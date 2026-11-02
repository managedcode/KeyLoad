using KeyLoad.Core;

namespace KeyLoad.Query.Search;

/// <summary>Waits for a physical projection owner before a new read acquires work.</summary>
internal static class TextProjectionAdmission
{
    internal static KeyLoadException Busy(string detail, Task availability)
    {
        var failure = Errors.Fail(ErrorCode.BudgetExceeded, detail);
        failure.Data /* KLD0041: The existing sealed KeyLoadException carries this local projection availability task in CLR exception metadata. */
            [typeof(TextProjectionAdmission)] /* KLD0041: Exception.Data requires an object-valued slot for this local projection availability Task reference. */ = availability;
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
                && (failure.Data /* KLD0041: Read the local projection availability signal from the existing CLR exception metadata contract. */
                    [typeof(TextProjectionAdmission)] /* KLD0041: The CLR metadata indexer returns the projection availability task as object. */
                    is Task availability /* KLD0042: Validate and bind the native exception metadata value to its required Task type once. */))
            {
                await availability.WaitAsync(cancellationToken);
            }
        }
    }
}
