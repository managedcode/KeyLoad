namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class C1OutcomeInspectionFailures
{
    internal static bool ContainsFatal(Exception failure)
    {
        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(failure);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current))
            {
                continue;
            }
            if (IsFatal(current))
            {
                return true;
            }
            PushInnerFailures(current, pending);
        }
        return false;
    }

    private static bool IsFatal(Exception failure)
        => failure is OutOfMemoryException or StackOverflowException or AccessViolationException;

    private static void PushInnerFailures(Exception failure, Stack<Exception> pending)
    {
        if (failure is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                pending.Push(inner);
            }
        }
        else if (failure.InnerException is { } inner)
        {
            pending.Push(inner);
        }
    }
}
