using System.Runtime.ExceptionServices;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class ExistingStoreInspectorFailureJoin
{
    internal static void Capture(Action action, List<Exception> failures)
    {
        try
        {
            Invoke(action);
        }
        catch (AggregateException owned)
        {
            Add(owned.InnerExceptions[0], failures);
        }
    }

    internal static async Task<bool> ObserveAsync(Task task, List<Exception> failures)
    {
        try
        {
            await AwaitAsync(task);
            return true;
        }
        catch (AggregateException owned)
        {
            Add(owned.InnerExceptions[0], failures);
            AddTaskFailures(task, failures);
            return false;
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

    internal static void Add(Exception original, List<Exception> failures)
    {
        if (!failures.Contains(original, ReferenceEqualityComparer.Instance))
        {
            failures.Add(original);
        }
    }

    private static void AddTaskFailures(Task task, List<Exception> failures)
    {
        if (task.Exception is not { } aggregate)
        {
            return;
        }
        foreach (var original in aggregate.InnerExceptions)
        {
            Add(original, failures);
        }
    }

    private static void Invoke(Action action)
    {
        try
        {
            action();
        }
        catch (Exception original)
        {
            throw new AggregateException(original);
        }
    }

    private static async Task AwaitAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception original)
        {
            throw new AggregateException(original);
        }
    }
}
