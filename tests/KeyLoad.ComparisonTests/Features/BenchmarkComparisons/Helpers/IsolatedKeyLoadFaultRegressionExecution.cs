using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Keeps native phase and cleanup failures coded while preserving each evidence and restoration attempt.</summary>
internal static class IsolatedKeyLoadFaultRegressionExecution
{
    internal static async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            throw new ComparisonFailureException(IsolatedKeyLoadFaultRegressionProtocol.Failure);
        }
    }
}
