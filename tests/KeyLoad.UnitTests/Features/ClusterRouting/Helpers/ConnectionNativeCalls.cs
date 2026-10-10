using System.Collections.Concurrent;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ConnectionNativeCalls : IDisposable
{
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentDictionary<Guid, Task<GrainOperationReply>> originals = new();
    private readonly HashSet<Task> unfinished = [];

    internal CancellationToken Token => stopping.Token;

    internal Task<GrainOperationReply> Retain(Guid requestId, Task<GrainOperationReply> original)
    {
        if (!originals.TryAdd(requestId, original))
        { throw new InvalidOperationException(ConnectionNativeProtocol.DuplicateObservation); }
        return original;
    }

    internal Task StopAsync()
    {
        foreach (var original in originals.Values)
        {
            if (!original.IsCompleted)
            { unfinished.Add(original); }
        }
        return stopping.CancelAsync();
    }

    internal async Task JoinAsync(List<Exception> failures, CancellationToken cancellationToken)
    {
        foreach (var original in originals.OrderBy(static pair => pair.Key))
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
        if (unfinished.Contains(original) && original.Exception is { } terminalFailure)
        { failures.AddRange(terminalFailure.InnerExceptions); }
    }

    internal void WriteTrace(string phase)
    {
        foreach (var original in originals.OrderBy(static pair => pair.Key))
        {
            Console.WriteLine(FormattableString.Invariant(
                $"ConnectionNative phase={phase} request={original.Key:D} caller={original.Value.Status}"));
        }
    }

    public void Dispose() => stopping.Dispose();
}
