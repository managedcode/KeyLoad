using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Owns native derived analytics inside the existing physical silo lifetime, with demand-only sampling.</summary>
internal sealed class OperationalAnalyticsRuntime(IOptions<OperationalAnalyticsOptions> options, TimeProvider clock)
    : IAsyncDisposable
{
    private const string Unavailable = "The node operational analytics owner is unavailable.";
    private const string Busy = "The node already has an operational analytics read in progress.";
    private const int NextHour = 1;
    private readonly Lock lifecycle = new();
    private OperationalAnalyticsOwner? owner;
    private Task? operation;
    private Task? shutdown;
    private bool acquisitionStarted;
    private bool nativeOperationStarted;
    private bool closed;

    internal bool CanReleasePhysical
    {
        get
        {
            lock (lifecycle)
            {
                return !acquisitionStarted || owner is { HasJoinedEngine: true };
            }
        }
    }

    internal async Task OpenAsync(PartitionHost partition)
    {
        OperationalAnalyticsOwner native;
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (acquisitionStarted)
            {
                throw new InvalidOperationException(Unavailable);
            }
            acquisitionStarted = true;
            native = new(partition.DirectoryPath, options, clock);
            owner = native;
        }
        var state = await partition.Consensus.StateAsync(CancellationToken.None).ConfigureAwait(false);
        if (!native.TryRecord(state))
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, Unavailable);
        }
        await native.FlushAsync(CancellationToken.None).ConfigureAwait(false);
    }

    internal Task<OperationalAnalyticsReport> ReadAsync(PartitionHost partition, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            var native = owner ?? throw new InvalidOperationException(Unavailable);
            if (operation is { IsCompleted: false })
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, Busy);
            }
            nativeOperationStarted = false;
            var original = ReadOriginalAsync(partition, native);
            operation = original;
            return original.WaitAsync(token);
        }
    }

    private async Task<OperationalAnalyticsReport> ReadOriginalAsync(PartitionHost partition, OperationalAnalyticsOwner native)
    {
        // This lease belongs to the original native flush/query, not the cancelable caller wait.
        using var admission = partition.Database.AdmitQuery(CancellationToken.None);
        var state = await partition.Consensus.StateAsync(CancellationToken.None).ConfigureAwait(false);
        if (!native.TryRecord(state, out var sampledAt))
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, Unavailable);
        }
        var from = new DateTimeOffset(sampledAt.UtcTicks - sampledAt.UtcTicks % TimeSpan.TicksPerHour, TimeSpan.Zero);
        lock (lifecycle)
        {
            nativeOperationStarted = true;
        }
        return await native.ReadAsync(from, from.AddHours(NextHour), CancellationToken.None).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            closed = true;
            return new(shutdown ??= CloseOriginalAsync(operation ?? Task.CompletedTask));
        }
    }

    private async Task CloseOriginalAsync(Task original)
    {
        await Task.Yield();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            {
                await original.ConfigureAwait(false);
            }
            catch (KeyLoadException error) when (error.Code == ErrorCode.ResourceExhausted && !nativeOperationStarted)
            {
                // A pre-query admission/Track refusal is an observed caller outcome, not failed native ownership.
            }
        }, failures).ConfigureAwait(false);
        if (owner is not null)
        {
            owner.CloseAdmission();
            try
            {
                await owner.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure) when (KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
        }
        if (!CanReleasePhysical)
        {
            failures.Add(Errors.Fail(ErrorCode.OwnershipLost, Unavailable));
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
