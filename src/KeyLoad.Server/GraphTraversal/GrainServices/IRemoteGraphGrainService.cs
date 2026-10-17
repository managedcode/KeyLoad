namespace KeyLoad.Server.Features.GraphTraversal;

[global::Orleans.Alias(RemoteGraphNativeProtocol.ServiceAlias)]
[global::Orleans.CodeGeneration.Version(1)]
internal interface IRemoteGraphGrainService : global::Orleans.Services.IGrainService
{
    [global::Orleans.Alias(RemoteGraphNativeProtocol.ExchangeAlias)]
    Task<RemoteGraphNativeReplyV1> ExchangeAsync(RemoteGraphNativeRequestV1 request,
        CancellationToken cancellationToken);
}
