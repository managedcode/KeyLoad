using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class OrleansNodeHostShutdown
{
    internal static async Task JoinAsync(IHost stopping, PartitionHost partition,
        NativeRequestWorkOwner requestWork, ServerRuntimeOptions runtimeOptions, TimeProvider runtimeClock,
        Action clearGrains, Action clearHost, List<Exception> failures)
    {
        var shutdownTimeout = runtimeOptions.Membership.Value.ShutdownTimeout;
        using var deadline = new CancellationTokenSource(shutdownTimeout, runtimeClock);
        await ServerFailureObserver.ObserveAsync(requestWork.DrainAsync, failures).ConfigureAwait(false);
        if (!requestWork.IsJoined)
        {
            ServerFailureObserver.ThrowIfAny(failures);
            throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RequestWorkNotJoined);
        }
        await JoinProducerDisposalAsync(stopping.Services, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => stopping.StopAsync(deadline.Token), failures).ConfigureAwait(false);
        stopping.Services.GetRequiredService<RuntimeJournalAdmission>().Close();
        clearGrains();
        // A failed native Start has no lifecycle rollback. Drain the endpoint while its transport still exists.
        ServerFailureObserver.Observe(() => stopping.Services.GetService<ReplicaSiloDiscoveryState>()?.StopDiscovery(), failures);
        await ServerFailureObserver.ObserveAsync(() => partition.Consensus.StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
        clearHost();
        if (stopping is IAsyncDisposable asynchronous)
        { await ServerFailureObserver.ObserveAsync(() => asynchronous.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        else
        { ServerFailureObserver.Observe(stopping.Dispose, failures); }
    }

    private static async Task JoinProducerDisposalAsync(IServiceProvider services, List<Exception> failures)
    {
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            if (services.GetService<RequestCqrsProbeNativeRelay>() is { } relay)
            { await relay.StopAsync().ConfigureAwait(false); }
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            if (services.GetService<RequestCqrsProbeObserver>() is { } observer)
            { await observer.DisposeAsync().ConfigureAwait(false); }
        }, failures).ConfigureAwait(false);
    }
}
