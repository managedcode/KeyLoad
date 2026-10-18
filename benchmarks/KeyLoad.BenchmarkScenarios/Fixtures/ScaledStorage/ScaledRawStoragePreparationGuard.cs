using Microsoft.Extensions.Options;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal static class ScaledRawStoragePreparationGuard
{
    private const string DeadlineMessage = "The scaled fixture preparation deadline expired.";

    internal static void Check(long deadlineStart, IOptions<ScaledStorageExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (timeProvider.GetElapsedTime(deadlineStart) >= executionOptions.Value.PreparationTimeout)
        {
            throw new TimeoutException(DeadlineMessage);
        }
    }
}
