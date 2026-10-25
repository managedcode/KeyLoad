using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class ConnectionCalls : IDisposable
{
    private readonly CancellationTokenSource stopping = new();
    private readonly Lock admission = new();
    private readonly Dictionary<Guid, Task<GrainOperationReply>> originals = [];
    private readonly HashSet<Guid> starting = [];
    private readonly HashSet<Task> unfinished = [];

    internal CancellationToken Token => stopping.Token;

    internal Task<GrainOperationReply> Retain(Guid requestId, Func<Task<GrainOperationReply>> start)
    {
        ArgumentNullException.ThrowIfNull(start);
        lock (admission)
        {
            if (originals.ContainsKey(requestId) || !starting.Add(requestId))
            {
                throw new InvalidOperationException(ConnectionProtocol.DuplicateObservation);
            }
            try
            {
                var original = start();
                originals.Add(requestId, original);
                return original;
            }
            finally
            {
                starting.Remove(requestId);
            }
        }
    }

    internal Task StopAsync()
    {
        lock (admission)
        {
            foreach (var original in originals.Values)
            {
                if (!original.IsCompleted)
                {
                    unfinished.Add(original);
                }
            }
        }
        return stopping.CancelAsync();
    }

    internal async Task JoinAsync(List<Exception> failures, CancellationToken cancellationToken)
    {
        foreach (var original in Snapshot().OrderBy(static pair => pair.Key))
        {
            await ServerFailureObserver.ObserveAsync(
                () => JoinOriginalAsync(original.Value, failures, cancellationToken), failures);
        }
    }

    private async Task JoinOriginalAsync(Task original, List<Exception> failures,
        CancellationToken cancellationToken)
    {
        var waiting = original.WaitAsync(cancellationToken);
        await waiting.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (!original.IsCompleted)
        {
            await ServerFailureObserver.ObserveAsync(() => waiting, failures);
            return;
        }
        if (WasUnfinished(original) && original.Exception is { } terminalFailure)
        {
            failures.AddRange(terminalFailure.InnerExceptions);
        }
    }

    internal void WriteTrace(string phase)
    {
        foreach (var original in Snapshot().OrderBy(static pair => pair.Key))
        {
            Console.WriteLine(FormattableString.Invariant(
                $"RuntimeConnection phase={phase} request={original.Key:D} caller={original.Value.Status}"));
        }
    }

    private KeyValuePair<Guid, Task<GrainOperationReply>>[] Snapshot()
    {
        lock (admission)
        {
            return originals.ToArray();
        }
    }

    private bool WasUnfinished(Task original)
    {
        lock (admission)
        {
            return unfinished.Contains(original);
        }
    }

    public void Dispose() => stopping.Dispose();
}
