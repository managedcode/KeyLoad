using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Coordinates a no-change real ZoneTree commit holding the exclusive store gate.</summary>
internal sealed class RealZoneTreeWriteGateHold : IAsyncDisposable
{
    private static readonly TimeSpan CoordinationTimeout = TimeSpan.FromSeconds(10);
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task holder;

    /// <summary>Starts a real no-change commit whose callback waits for explicit release.</summary>
    internal RealZoneTreeWriteGateHold(ZoneTreeStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        holder = Task.Run(() => store.Commit((_, _) =>
        {
            entered.SetResult();
            release.Task.GetAwaiter().GetResult();
            return true;
        }));
    }

    /// <summary>Waits until the commit callback owns the exclusive store gate.</summary>
    internal Task WaitUntilEnteredAsync() => entered.Task.WaitAsync(CoordinationTimeout);

    /// <summary>Releases the callback and waits for the real commit to exit.</summary>
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
