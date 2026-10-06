using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.CodeQuality.Processes;

internal sealed class NativeProcessCleanupDeadline : IAsyncDisposable
{
    private const string FailStopMessage = "An owned native process or its original readers did not settle within the cleanup bound.";
    private const int FailStopExitCode = 1;

    private readonly TimeProvider timeProvider;
    private readonly TimeSpan timeout;
    private readonly long startedAt;
    private readonly CancellationTokenSource expirationCancellation = new();
    private readonly Task expiration;
    private bool disposed;

    internal NativeProcessCleanupDeadline(TimeSpan timeout, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero, nameof(timeout));
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.timeout = timeout;
        this.timeProvider = timeProvider;
        startedAt = timeProvider.GetTimestamp();
        expiration = Task.Delay(timeout, timeProvider, expirationCancellation.Token);
    }

    internal async Task<bool> TryJoinAsync(Task original, TimeSpan? initialWait = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.IsCompleted)
        {
            return !IsExpired;
        }
        var remaining = Remaining;
        if (remaining <= TimeSpan.Zero)
        {
            return false;
        }
        var window = initialWait is { } requested && requested < remaining ? requested : remaining;
        if (window == remaining)
        {
            return await Task.WhenAny(original, expiration).ConfigureAwait(false) == original && !IsExpired;
        }
        return await JoinDuringPollWindowAsync(original, window).ConfigureAwait(false);
    }

    internal async Task ConfirmNativeExitAsync(Process process, TimeSpan pollInterval)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pollInterval, TimeSpan.Zero, nameof(pollInterval));
        while (!process.HasExited)
        {
            if (!await WaitForPollAsync(pollInterval).ConfigureAwait(false))
            {
                FailStop();
            }
        }
        if (IsExpired)
        {
            FailStop();
        }
    }

    internal static void FailStop()
    {
        try
        {
            Console.Error.WriteLine(FailStopMessage);
        }
        finally
        {
            Environment.Exit(FailStopExitCode);
        }
        throw new InvalidOperationException(FailStopMessage);
    }

    private async Task<bool> JoinDuringPollWindowAsync(Task original, TimeSpan window)
    {
        using var cancellation = new CancellationTokenSource();
        var poll = Task.Delay(window, timeProvider, cancellation.Token);
        try
        {
            _ = await Task.WhenAny(original, poll, expiration).ConfigureAwait(false);
            return original.IsCompleted && !IsExpired;
        }
        finally
        {
            await CancelAndJoinAsync(cancellation, poll).ConfigureAwait(false);
        }
    }

    private async Task<bool> WaitForPollAsync(TimeSpan interval)
    {
        var remaining = Remaining;
        if (remaining <= TimeSpan.Zero)
        {
            return false;
        }
        if (interval >= remaining)
        {
            await expiration.ConfigureAwait(false);
            return false;
        }
        using var cancellation = new CancellationTokenSource();
        var poll = Task.Delay(interval, timeProvider, cancellation.Token);
        try
        {
            return await Task.WhenAny(poll, expiration).ConfigureAwait(false) == poll;
        }
        finally
        {
            await CancelAndJoinAsync(cancellation, poll).ConfigureAwait(false);
        }
    }

    private static async Task CancelAndJoinAsync(CancellationTokenSource cancellation, Task poll)
    {
        try
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            await poll.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private bool IsExpired => timeProvider.GetElapsedTime(startedAt) >= timeout;

    private TimeSpan Remaining
    {
        get
        {
            var elapsed = timeProvider.GetElapsedTime(startedAt);
            return elapsed >= timeout ? TimeSpan.Zero : timeout - elapsed;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        try
        {
            await expirationCancellation.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            await expiration.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            expirationCancellation.Dispose();
        }
    }
}
