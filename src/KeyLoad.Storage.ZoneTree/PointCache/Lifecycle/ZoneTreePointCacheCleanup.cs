using System.Runtime.ExceptionServices;

namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>Retains admission-close and independent physical/cache cleanup failures.</summary>
internal static class ZoneTreePointCacheCleanup
{
    private const int SingleFailureCount = 1;
    private const int FirstFailureIndex = 0;

    internal static void Capture(Action action, List<Exception> failures)
    {
        try
        {
            RunOrWrap(action);
        }
        catch (AggregateException failure)
        {
            failures.AddRange(failure.Flatten().InnerExceptions);
        }
    }

    internal static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == SingleFailureCount)
        {
            ExceptionDispatchInfo.Capture(failures[FirstFailureIndex]).Throw();
        }
        if (failures.Count > SingleFailureCount)
        {
            throw new AggregateException(failures);
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
