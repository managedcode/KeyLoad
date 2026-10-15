namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Publishes the same application silo after Active and joins borrowed work before native teardown.</summary>
internal sealed class OrleansNodeLifecycle(OrleansNode node, ServerConnectionRegistry connections) : ILifecycleParticipant<ISiloLifecycle>
{
    private const int PublicationStageOffset = 1;

    public void Participate(ISiloLifecycle lifecycle)
        => lifecycle.Subscribe(nameof(OrleansNodeLifecycle), ServiceLifecycleStage.Active + PublicationStageOffset,
            token =>
            {
                node.PublishNativeRuntime(token);
                return Task.CompletedTask;
            }, _ => StopAsync());

    private async Task StopAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => connections.ShutdownAsync(CancellationToken.None), failures)
            .ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => node.StopAsync(CancellationToken.None), failures)
            .ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
