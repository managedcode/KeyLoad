using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.GraphTraversal;

internal sealed class RemoteGraphGrainService(GrainId id, Silo silo, ILoggerFactory loggerFactory)
    : GrainService(id, silo, loggerFactory), IRemoteGraphGrainService
{
    private RemoteGraphNativeRuntime? receiver;

    public override async Task Init(IServiceProvider serviceProvider)
    {
        await base.Init(serviceProvider).ConfigureAwait(true);
        var token = StoppedCancellationTokenSource.Token;
        await serviceProvider.GetRequiredService<Func<IPartitionStoragePublication>>()().WaitForOpenAsync(token).ConfigureAwait(true);
        await serviceProvider.GetRequiredService<OrleansNode>().InitializeLocalRuntimeAsync(token).ConfigureAwait(true);
        receiver = serviceProvider.GetRequiredService<RemoteGraphNativeRuntime>();
    }

    public Task<RemoteGraphNativeReplyV1> ExchangeAsync(RemoteGraphNativeRequestV1 request,
        CancellationToken cancellationToken)
        => (receiver ?? throw new InvalidOperationException(RemoteGraphNativeProtocol.Unavailable))
            .ExchangeAsync(request, cancellationToken);

    public override async Task Stop()
    {
        var failures = new List<Exception>();
        if (receiver is { } actual)
        {
            await ServerFailureObserver.ObserveAsync(actual.StopAsync, failures).ConfigureAwait(true);
        }
        await ServerFailureObserver.ObserveAsync(base.Stop, failures).ConfigureAwait(true);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
