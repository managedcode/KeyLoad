using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteModelGrainService(GrainId grainId, Silo silo, ILoggerFactory loggerFactory) : GrainService(grainId, silo, loggerFactory), IRemoteModelGrainService
{
    private RemoteModelNativeRuntime? runtime;

    public override async Task Init(IServiceProvider serviceProvider)
    {
        await base.Init(serviceProvider).ConfigureAwait(true);
        var token = StoppedCancellationTokenSource.Token;
        await serviceProvider.GetRequiredService<Func<IPartitionStoragePublication>>()().WaitForOpenAsync(token).ConfigureAwait(true);
        await serviceProvider.GetRequiredService<OrleansNode>().InitializeLocalRuntimeAsync(token).ConfigureAwait(true);
        runtime = serviceProvider.GetRequiredService<RemoteModelNativeRuntime>();
    }

    public async Task<RemoteModelNativeReply> ExchangeAsync(RemoteModelNativeRequest request, CancellationToken cancellationToken)
    {
        var active = runtime ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteModelNativeProtocol.Unavailable);
        RemoteModelNativeReply? original = null;
        await global::ManagedCode.Orleans.Graph.Extensions.RequestContextHelper.RunWithCurrentCallerAsync(
            typeof(RemoteModelGrainService).FullName!, nameof(IRemoteModelGrainService.ExchangeAsync),
            async () => { original = await active.ExchangeAsync(request, cancellationToken); });
        return original ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteModelNativeProtocol.Unavailable);
    }
}
