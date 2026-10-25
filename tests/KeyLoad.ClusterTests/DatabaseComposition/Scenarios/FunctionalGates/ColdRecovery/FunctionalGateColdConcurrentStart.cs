namespace KeyLoad.ClusterTests.DatabaseComposition;

/// <summary>Releases one fixed eight-caller wave only after every actual owned public session is ready.</summary>
internal sealed class FunctionalGateColdConcurrentStart
{
    private const string TooManyCallers = "The original cold wave contains too many public callers.";
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int arrivals;

    internal int Arrivals => Volatile.Read(ref arrivals);

    internal async Task EnterAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var count = Interlocked.Add(ref arrivals, 1);
        if (count > FunctionalGateColdWorkloadProvision.TotalWorkloadIdentities)
        {
            throw new InvalidOperationException(TooManyCallers);
        }
        if (count == FunctionalGateColdWorkloadProvision.TotalWorkloadIdentities)
        {
            ready.TrySetResult();
        }
        await ready.Task.WaitAsync(token);
        token.ThrowIfCancellationRequested();
    }
}
