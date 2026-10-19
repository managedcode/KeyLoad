namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeReadCutCleanup
{
    internal static void Capture(Action action, List<Exception> failures)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(failures);
        try
        {
            RunOrWrap(action);
        }
        catch (AggregateException failure)
        {
            failures.AddRange(failure.InnerExceptions);
        }
    }

    private static void RunOrWrap(Action action)
    {
        try
        {
            action();
        }
        catch (Exception failure)
        {
            throw new AggregateException(failure);
        }
    }
}
