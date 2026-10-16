using ManagedCode.Communication.CQRS;

namespace KeyLoad.Replication;

/// <summary>Retains the signed forward deadline until the original queue, work and publication have joined.</summary>
internal static class ReplicaForwardRequestLifetime
{
    private const int NoRemainingTicks = 0;

    internal static async Task<T> ExecuteAsync<T>(DateTimeOffset? expiresAt, TimeProvider clock,
        Func<CancellationToken, Task<T>> execute, CancellationToken incoming)
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
        return await ExecuteOwnedAsync(remaining, clock, execute, incoming).ConfigureAwait(false);
    }

    private static async Task<T> ExecuteOwnedAsync<T>(TimeSpan remaining, TimeProvider clock,
        Func<CancellationToken, Task<T>> execute, CancellationToken incoming)
    {
        var failures = new List<Exception>();
        T? result = default;
        try
        {
            using var deadline = new CancellationTokenSource(remaining, clock);
            try
            {
                result = await ExecuteLinkedAsync(deadline, execute, failures, incoming).ConfigureAwait(false);
            }
            catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null)
            {
                failures.Add(error);
            }
            catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null)
            {
                failures.Add(error);
            }
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null)
        {
            failures.Add(error);
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            failures.Add(error);
        }
        ReplicaShutdownStage.ThrowIfAny(failures);
        return result!;
    }

    private static async Task<T> ExecuteLinkedAsync<T>(CancellationTokenSource deadline,
        Func<CancellationToken, Task<T>> execute, List<Exception> failures, CancellationToken incoming)
    {
        CancellationTokenSource? linked = null;
        T? result = default;
        try
        {
            linked = CancellationTokenSource.CreateLinkedTokenSource(incoming, deadline.Token);
            result = await execute(linked.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null)
        {
            failures.Add(error);
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            failures.Add(error);
        }
        finally
        {
            try
            {
                await CancelAsync(deadline, linked, failures).ConfigureAwait(false);
            }
            finally
            {
                linked?.Dispose();
            }
        }
        return result!;
    }

    private static async Task CancelAsync(CancellationTokenSource? deadline, CancellationTokenSource? linked,
        List<Exception> failures)
    {
        try
        {
            await CancelOneAsync(deadline, failures).ConfigureAwait(false);
        }
        finally
        {
            await CancelOneAsync(linked, failures).ConfigureAwait(false);
        }
    }

    private static async Task CancelOneAsync(CancellationTokenSource? source, List<Exception> failures)
    {
        try
        {
            if (source is not null)
            {
                await ReplicaShutdownStage.ObserveAsync(source.CancelAsync(), failures).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null)
        {
            failures.Add(error);
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            failures.Add(error);
        }
    }
}
