using System.Runtime.ExceptionServices;

namespace KeyLoad.Comparisons.Targets;

internal static class KurrentCleanupFatalCause
{
    private const int FirstAggregateIndex = 0;
    private const int PreviousIndexOffset = 1;

    internal static ExceptionDispatchInfo? Find(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Exception>();
        pending.Push(error);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current))
            {
                continue;
            }
            if (IsFatal(current))
            {
                return ExceptionDispatchInfo.Capture(current);
            }
            if (current is AggregateException aggregate)
            {
                for (var index = aggregate.InnerExceptions.Count - PreviousIndexOffset;
                    index >= FirstAggregateIndex; index--)
                {
                    pending.Push(aggregate.InnerExceptions[index]);
                }
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push(inner);
            }
        }
        return null;
    }

    private static bool IsFatal(Exception error)
        => error is OutOfMemoryException or StackOverflowException or AccessViolationException
            or AppDomainUnloadedException or BadImageFormatException or CannotUnloadAppDomainException;
}
