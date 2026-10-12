namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteModelGrainService(GrainId grainId, Silo silo, ILoggerFactory loggerFactory,
    RemoteModelNativeRuntime runtime) : GrainService(grainId, silo, loggerFactory), IRemoteModelGrainService
{
    private readonly RemoteModelNativeRuntime runtime = runtime;

    public Task<RemoteModelNativeReply> ExchangeAsync(RemoteModelNativeRequest request, CancellationToken cancellationToken)
        => runtime.ExchangeAsync(request, cancellationToken);
}
