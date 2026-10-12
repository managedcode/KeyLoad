namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteModelGrainService(GrainId grainId, Silo silo, ILoggerFactory loggerFactory,
    RemoteModelNativeRuntime runtime) : GrainService(grainId, silo, loggerFactory), IRemoteModelGrainService
{
    private readonly RemoteModelNativeRuntime runtime = runtime;

    public async Task<RemoteModelNativeReply> ExchangeAsync(RemoteModelNativeRequest request, CancellationToken cancellationToken)
    {
        RemoteModelNativeReply? original = null;
        await global::ManagedCode.Orleans.Graph.Extensions.RequestContextHelper.RunWithCurrentCallerAsync(
            typeof(RemoteModelGrainService).FullName!, nameof(IRemoteModelGrainService.ExchangeAsync),
            async () => { original = await runtime.ExchangeAsync(request, cancellationToken); });
        return original ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteModelNativeProtocol.Unavailable);
    }
}
