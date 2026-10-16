using KeyLoad.Core;

namespace KeyLoad.Replication;

/// <summary>Retains the signed forward deadline until the original queue, work and publication have joined.</summary>
internal static class ReplicaForwardRequestLifetime
{
    private const int NoRemainingTicks = 0;

    internal static async Task<T> ExecuteAsync<T>(DateTimeOffset? expiresAt, TimeProvider clock,
        CancellationToken incoming, Func<CancellationToken, Task<T>> execute)
    {
        if (expiresAt is null)
        {
            return await execute(incoming).ConfigureAwait(false);
        }
        var remaining = expiresAt.Value - clock.GetUtcNow();
        if (remaining.Ticks <= NoRemainingTicks)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ReplicaProtocol.InvalidPeer);
        }
        CancellationTokenSource? deadline = null;
        CancellationTokenSource? linked = null;
        var failures = new List<Exception>();
        T? result = default;
        try
        {
            deadline = new(remaining, clock);
            linked = CancellationTokenSource.CreateLinkedTokenSource(incoming, deadline.Token);
            result = await execute(linked.Token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
        await CancelAsync(deadline, failures).ConfigureAwait(false);
        await CancelAsync(linked, failures).ConfigureAwait(false);
        Dispose(linked, failures);
        Dispose(deadline, failures);
        ReplicaShutdownStage.ThrowIfAny(failures);
        return result!;
    }

    private static async Task CancelAsync(CancellationTokenSource? source, List<Exception> failures)
    {
        try
        {
            if (source is not null)
            {
                await ReplicaShutdownStage.ObserveAsync(source.CancelAsync(), failures).ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
    }

    private static void Dispose(CancellationTokenSource? source, List<Exception> failures)
    {
        try
        {
            source?.Dispose();
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
    }
}
