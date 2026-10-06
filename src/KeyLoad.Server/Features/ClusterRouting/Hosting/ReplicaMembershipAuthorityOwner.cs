using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal sealed class ReplicaMembershipAuthorityOwner : IAsyncDisposable
{
    private readonly Lock sync = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource startShutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IMembershipTable? provider;
    private Task? shutdown;
    private const int NoActiveWork = 0;
    private int active;
    private bool admissionClosed;
    private bool disposed;

    internal bool IsAdmissionClosed
    {
        get { lock (sync) { return admissionClosed; } }
    }

    internal bool IsDisposed
    {
        get { lock (sync) { return disposed; } }
    }

    internal bool IsReady
    {
        get { lock (sync) { return provider is not null && !admissionClosed; } }
    }

    internal void Publish(IMembershipTable table)
    {
        const string PublishFailureMessage = "The membership authority provider cannot be published.";

        ArgumentNullException.ThrowIfNull(table);
        lock (sync)
        {
            if (provider is not null || admissionClosed)
            { throw new InvalidOperationException(PublishFailureMessage); }
            provider = table;
        }
    }

    internal Operation? TryEnter()
    {
        lock (sync)
        {
            if (admissionClosed || provider is null)
            { return null; }
            var operation = new Operation(this, provider, stopping.Token);
            active++;
            return operation;
        }
    }

    internal Task StopAdmissionAndJoinAsync()
    {
        Task work;
        lock (sync)
        {
            if (shutdown is not null)
            { return shutdown; }
            admissionClosed = true;
            if (active == NoActiveWork)
            { drained.TrySetResult(); }
            shutdown = StopCoreAsync();
            work = shutdown;
        }
        startShutdown.TrySetResult();
        return work;
    }

    internal void ClearProvider()
    {
        const int EmptyActive = 0;
        const string ClearProviderFailureMessage = "Membership authority operations have not joined.";

        lock (sync)
        {
            if (!admissionClosed || active != EmptyActive)
            { throw Errors.Fail(ErrorCode.OwnershipLost, ClearProviderFailureMessage); }
            provider = null;
        }
    }

    internal async Task StopAdmissionJoinAndClearProviderAsync(List<Exception> failures)
    {
        await ServerFailureObserver.ObserveAsync(StopAdmissionAndJoinAsync, failures).ConfigureAwait(false);
        try
        { ClearProvider(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await StopAdmissionJoinAndClearProviderAsync(failures).ConfigureAwait(false);
        try
        { stopping.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
        lock (sync)
        { disposed = true; }
    }

    private async Task StopCoreAsync()
    {
        await startShutdown.Task.ConfigureAwait(false);
        const int ShutdownObservationStages = 2;
        var failures = new List<Exception>(ShutdownObservationStages);
        await ServerFailureObserver.ObserveAsync(stopping.CancelAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => drained.Task, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void Exit()
    {
        const int EmptyActive = 0;

        lock (sync)
        {
            active--;
            if (active == EmptyActive && admissionClosed)
            { drained.TrySetResult(); }
        }
    }

    internal sealed class Operation : IDisposable
    {
        private ReplicaMembershipAuthorityOwner? owner;

        internal Operation(ReplicaMembershipAuthorityOwner owner, IMembershipTable provider, CancellationToken shutdownToken)
        {
            this.owner = owner;
            Provider = provider;
            ShutdownToken = shutdownToken;
        }

        internal IMembershipTable Provider { get; }
        internal CancellationToken ShutdownToken { get; }

        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Exit();
    }
}
