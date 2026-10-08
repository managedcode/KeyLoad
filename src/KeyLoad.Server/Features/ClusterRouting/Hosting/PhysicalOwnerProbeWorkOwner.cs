using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PhysicalOwnerProbeWorkOwner(IOptions<GrainRoutingOptions> routingOptions,
    IOptions<NodeOptions> nodeOptions, IOptions<PhysicalOwnerExecutionOptions> executionOptions) : IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly NativeRequestWorkOwner? work = nodeOptions.Value.MembershipAuthority.RegisterPhysicalOwners ? new(routingOptions) : null;
    private readonly SemaphoreSlim? admissions = nodeOptions.Value.MembershipAuthority.RegisterPhysicalOwners
        ? new(executionOptions.Value.MaximumAdmissions, executionOptions.Value.MaximumAdmissions) : null;
    private bool closed;
    private Task? disposal;

    internal Operation Acquire(Guid requestId)
    {
        lock (gate)
        {
            if (work is null || admissions is null)
            { throw Errors.Fail(ErrorCode.UnsupportedCapability, PhysicalOwnerProbeProtocol.Unavailable); }
            if (closed)
            { throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable); }
            if (!admissions.Wait(TimeSpan.Zero))
            { throw Errors.Fail(ErrorCode.ResourceExhausted, PhysicalOwnerProbeProtocol.Unavailable); }
            try
            { return new(ReleaseAdmission, work.Acquire(requestId, NativeRequestWorkKind.ReadCapability), work.ShutdownToken); }
            catch (Exception error)
            {
                var failures = new List<Exception> { error };
                try
                { admissions.Release(); }
                catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
                catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
                ServerFailureObserver.ThrowIfAny(failures);
                throw;
            }
        }
    }

    internal Task StopAsync()
    {
        lock (gate)
        { closed = true; return work?.DrainAsync() ?? Task.CompletedTask; }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        { disposal ??= DisposeCoreAsync(); return new ValueTask(disposal); }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(StopAsync, failures).ConfigureAwait(false);
        if (work is not null)
        {
            try
            { await work.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        if (admissions is not null)
        {
            try
            { admissions.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void ReleaseAdmission() => admissions!.Release();

    internal sealed class Operation(Action releaseAdmission, NativeRequestWorkLease lease,
        CancellationToken shutdownToken) : IDisposable
    {
        private Action? release = releaseAdmission;
        private NativeRequestWorkLease? workLease = lease;
        internal CancellationToken ShutdownToken { get; } = shutdownToken;
        public void Dispose()
        {
            var returnPermit = Interlocked.Exchange(ref release, null);
            if (returnPermit is null)
            { return; }
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(returnPermit, failures);
            try
            { workLease?.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            finally { workLease = null; }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }
}
