using Orleans.Runtime.Services;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsProbeGrainServiceClient(IServiceProvider services)
    : GrainServiceClient<IRequestCqrsProbeGrainService>(services)
{
    internal Task<RequestCqrsProbeNativeReply> ExchangeAsync(SiloAddress address, RequestCqrsProbeNativeRequest request,
        CancellationToken token)
        => request.Method == RequestCqrsProbeNativeMethod.Locate
            ? GetGrainService(address).LocateProducerClaimAsync(request, token)
            : GetGrainService(address).NotifyProducerDisposedAsync(request, token);
}
