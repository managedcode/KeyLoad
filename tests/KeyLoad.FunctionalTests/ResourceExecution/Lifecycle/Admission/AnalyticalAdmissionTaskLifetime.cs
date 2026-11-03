namespace KeyLoad.FunctionalTests.ResourceExecution;

/// <summary>Joins admitted work after cancellation and release while preserving every original failure.</summary>
internal static class AnalyticalAdmissionTaskLifetime
{
    private const string CleanupFailureMessage = "Analytical admission cleanup failed.";

    internal static async Task RunWithCleanupAsync(Func<Task> operation, Func<Task> cancel, Func<Task> release,
        Func<Task> observeAdmittedWorker)
    {
        var failures = new List<Exception>();
        await CaptureFailureAsync(operation, failures);
        await CaptureFailureAsync(cancel, failures);
        await CaptureFailureAsync(release, failures);
        await CaptureFailureAsync(observeAdmittedWorker, failures);
        if (failures.Count != 0)
        {
            throw new AggregateException(CleanupFailureMessage, failures);
        }
    }

    private static async Task CaptureFailureAsync(Func<Task> operation, List<Exception> failures)
    {
        try
        {
            await InvokeWithFailureWrapperAsync(operation);
        }
        catch (AggregateException error)
        {
            failures.AddRange(error.InnerExceptions);
        }
    }

    private static async Task InvokeWithFailureWrapperAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception error)
        {
            throw new AggregateException(CleanupFailureMessage, error);
        }
    }
}
