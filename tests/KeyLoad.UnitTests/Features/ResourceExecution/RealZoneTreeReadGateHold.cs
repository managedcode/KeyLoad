using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Coordinates a real ZoneTree read callback holding its storage gate.</summary>
internal sealed class RealZoneTreeReadGateHold : IAsyncDisposable
{
    private static readonly TimeSpan CoordinationTimeout = TimeSpan.FromSeconds(10);
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task holder;

    /// <summary>Starts a real read whose callback waits for explicit release.</summary>
    internal RealZoneTreeReadGateHold(ZoneTreeStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        holder = Task.Run(() => store.Read(_ =>
        {
            entered.SetResult();
            release.Task.GetAwaiter().GetResult();
            return true;
        }));
    }

    /// <summary>Waits until the real storage read callback owns the gate.</summary>
    internal Task WaitUntilEnteredAsync() => entered.Task.WaitAsync(CoordinationTimeout);

    /// <summary>Releases the callback and waits for the real read to exit.</summary>
    internal async Task ReleaseAsync()
    {
        release.TrySetResult();
        try
        {
            await holder.WaitAsync(CoordinationTimeout);
        }
        finally
        {
            await holder;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await ReleaseAsync();
}
