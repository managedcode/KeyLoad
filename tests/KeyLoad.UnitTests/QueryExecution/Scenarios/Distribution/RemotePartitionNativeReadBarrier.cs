namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>Joins two actual independent native read observations under the original fixture deadline.</summary>
internal sealed class RemotePartitionNativeReadBarrier(CancellationTokenSource caller, CancellationToken fixtureToken)
{
    private const int BothOwners = 2;
    private readonly TaskCompletionSource observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int arrivals;
    internal int Arrivals => Volatile.Read(ref arrivals);

    internal void Arrive()
    {
        if (Interlocked.Increment(ref arrivals) == BothOwners)
        {
            observed.TrySetResult();
            caller.CancelAsync().GetAwaiter().GetResult();
        }
        observed.Task.WaitAsync(fixtureToken).GetAwaiter().GetResult();
    }
}
