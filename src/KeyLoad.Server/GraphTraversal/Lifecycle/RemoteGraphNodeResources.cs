namespace KeyLoad.Server.Features.GraphTraversal;

internal sealed class RemoteGraphNodeResources
{
    private RemoteGraphRuntime? runtime;
    private RemoteGraphNativeRuntime? receiver;
    private RemoteGraphNativeClient? client;

    internal RemoteGraphRuntime? Runtime => Volatile.Read(ref runtime);

    internal bool IsJoined => (Runtime?.IsJoined ?? true)
        && (Volatile.Read(ref receiver)?.IsJoined ?? true)
        && (Volatile.Read(ref client)?.IsJoined ?? true);

    internal async Task InitializeAsync(OrleansNode node, PartitionHost partition,
        ServerRuntimeOptions options, TimeProvider clock)
        => Volatile.Write(ref runtime, await RemoteGraphRuntime.CreateAsync(node, partition,
            options, clock).ConfigureAwait(false));

    internal void Capture(IServiceProvider services)
    {
        Volatile.Write(ref receiver, services.GetRequiredService<RemoteGraphNativeRuntime>());
        Volatile.Write(ref client, services.GetRequiredService<RemoteGraphNativeClient>());
    }

    internal void Attach()
        => Runtime?.AttachNativeReceiver(Volatile.Read(ref receiver)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable));

    internal RemoteGraphNativeClient BorrowClient()
        => Volatile.Read(ref client)
            ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, OrleansNodeProtocol.RoutingUnavailable);

    internal async Task JoinAsync(List<Exception> failures)
    {
        if (Runtime is { } originalRuntime)
        {
            await ServerFailureObserver.ObserveAsync(() => originalRuntime.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        if (Volatile.Read(ref receiver) is { } originalReceiver)
        {
            await ServerFailureObserver.ObserveAsync(originalReceiver.StopAsync, failures).ConfigureAwait(false);
        }
        if (Volatile.Read(ref client) is { } originalClient)
        {
            await ServerFailureObserver.ObserveAsync(originalClient.StopAsync, failures).ConfigureAwait(false);
        }
    }
}
