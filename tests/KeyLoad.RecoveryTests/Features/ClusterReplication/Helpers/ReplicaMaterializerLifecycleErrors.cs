using System.Runtime.ExceptionServices;

namespace KeyLoad.RecoveryTests;

internal static class ReplicaMaterializerLifecycleErrors
{
    internal static async Task AttemptAsync(Func<Task> action, List<Exception> failures)
    {
        try
        { await RunOrWrapAsync(action); }
        catch (AggregateException wrapper)
        { AddWrapped(wrapper, failures); }
    }

    internal static void Attempt(Action action, List<Exception> failures)
    {
        try
        { RunOrWrap(action); }
        catch (AggregateException wrapper)
        { AddWrapped(wrapper, failures); }
    }

    internal static void AddWrapped(AggregateException wrapper, List<Exception> failures)
    {
        foreach (var original in wrapper.InnerExceptions)
        {
            Add(original, failures);
        }
    }

    internal static void Add(Exception error, List<Exception> failures)
    {
        if (!failures.Contains(error))
        {
            failures.Add(error);
        }
    }

    internal static void Throw(List<Exception> failures)
    {
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }
    }

    private static async Task RunOrWrapAsync(Func<Task> action)
    {
        try
        { await action(); }
        catch (Exception original)
        { throw new AggregateException(original); }
    }

    private static void RunOrWrap(Action action)
    {
        try
        { action(); }
        catch (Exception original)
        { throw new AggregateException(original); }
    }
}
