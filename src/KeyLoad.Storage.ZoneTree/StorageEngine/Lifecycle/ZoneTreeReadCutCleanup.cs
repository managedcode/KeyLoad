namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeReadCutCleanup
{
    internal static void Capture(Action action, List<Exception> failures)
    {
        ArgumentNullException.ThrowIfNull(action) /* KLD0041: BCL null validation accepts the action Action reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(failures) /* KLD0041: BCL null validation accepts the failures List reference through its object parameter. */;
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
