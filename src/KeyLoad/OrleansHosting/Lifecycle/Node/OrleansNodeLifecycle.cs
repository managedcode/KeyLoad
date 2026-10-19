namespace KeyLoad.Server.Features.OrleansHosting;

/// <summary>Publishes the same application silo after Active and joins borrowed work before execution teardown.</summary>
internal sealed class OrleansNodeLifecycle(OrleansNode node, ServerConnectionRegistry connections) : ILifecycleParticipant<ISiloLifecycle>, IHostedService
{
    private const int PublicationStageOffset = 1;

    public void Participate(ISiloLifecycle lifecycle)
        => lifecycle.Subscribe(nameof(OrleansNodeLifecycle), ServiceLifecycleStage.Active + PublicationStageOffset,
            token =>
            {
                node.PublishRuntime(token);
                return Task.CompletedTask;
            }, _ => StopAsync(CancellationToken.None));

    public Task StartAsync(CancellationToken cancellationToken) => node.StartAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => connections.ShutdownAsync(cancellationToken), failures);
        await ServerFailureObserver.ObserveAsync(() => connections.ShutdownAsync(CancellationToken.None), failures);
        await ServerFailureObserver.ObserveAsync(() => node.StopAsync(cancellationToken), failures);
        await ServerFailureObserver.ObserveAsync(() => node.StopAsync(CancellationToken.None), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
