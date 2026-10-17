using Orleans.CodeGeneration;
using Orleans.Services;

namespace KeyLoad.Server.Features.DocumentStorage;

/// <summary>One bounded native exchange against this silo's original node-local model runtime.</summary>
[Alias(RemoteModelNativeProtocol.ServiceAlias), Version(RemoteModelNativeProtocol.InterfaceVersion)]
internal interface IRemoteModelGrainService : IGrainService
{
    /// <summary>Verifies current physical and silo ownership before original model dispatch.</summary>
    [Alias(RemoteModelNativeProtocol.ExchangeAlias)]
    Task<RemoteModelNativeReply> ExchangeAsync(RemoteModelNativeRequest request,
        CancellationToken cancellationToken);
}
