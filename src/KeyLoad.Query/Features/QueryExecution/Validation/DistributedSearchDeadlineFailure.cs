namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedSearchDeadlineFailure
{
    private const int NoFailures = 0;

    internal static bool IsOwnedDeadline(Exception cause, CancellationToken deadline, CancellationToken original)
    {
        if (!deadline.IsCancellationRequested || original.IsCancellationRequested)
        { return false; }
        var pending = new Stack<Exception>();
        pending.Push(cause);
        var foundCancellation = false;
        while (pending.TryPop(out var actual))
        {
            if (actual is KeyLoadException { Code: ErrorCode.Cancelled, InnerException: { } inner })
            { pending.Push(inner); }
            if (actual is KeyLoadException { Code: ErrorCode.Cancelled })
            {
                foundCancellation = true;
                continue;
            }
            if (actual is not AggregateException aggregate || aggregate.InnerExceptions.Count == NoFailures)
            { return false; }
            foreach (var failure in aggregate.InnerExceptions)
            { pending.Push(failure); }
        }
        return foundCancellation;
    }
}
