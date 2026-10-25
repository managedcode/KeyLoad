namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class ReadCutShutdownJoin
{
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(10);
    private const string Unjoined = "Execution read-cut shutdown failed to join its original tasks.";
    internal static async Task JoinOriginalTasksAsync(Task traversal, Task disposal)
    {
        var joined = Task.WhenAll(traversal, disposal);
        var timedOut = false;
        try
        {
            await joined.WaitAsync(JoinTimeout, TimeProvider.System);
        }
        catch (TimeoutException)
        {
            timedOut = true;
            await ObserveTaskAsync(joined);
        }
        catch (OperationCanceledException)
        {
        }
        if (timedOut)
        {
            throw new InvalidOperationException(Unjoined);
        }
    }

    internal static async Task ObserveTaskAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
