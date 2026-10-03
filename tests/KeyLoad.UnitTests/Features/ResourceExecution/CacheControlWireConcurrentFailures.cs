namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireConcurrentFailures
{
    private readonly HashSet<Task> collectedTasks = [];
    private readonly HashSet<Exception> knownFailures = new(ReferenceEqualityComparer.Instance);
    private readonly List<Exception> failures = [];

    internal void Add(Exception error)
    {
        if (knownFailures.Add(error))
        {
            failures.Add(error);
        }
    }

    internal void Collect(Task task, bool reportCancellation = true)
    {
        if (!task.IsCompleted || !collectedTasks.Add(task))
        {
            return;
        }

        if (task.IsFaulted)
        {
            foreach (var error in task.Exception!.Flatten().InnerExceptions)
            {
                Add(error);
            }
        }
        else if (reportCancellation && task.IsCanceled)
        {
            Add(new TaskCanceledException(task));
        }
    }

    internal void ThrowIfAny()
    {
        if (failures.Count > 0)
        {
            throw new AggregateException(FailureMessage, failures);
        }
    }

    private const string FailureMessage = "Concurrent signer operations or cleanup failed.";
}
