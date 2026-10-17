using Orleans.CodeGeneration;
using Orleans.Services;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Directed bounded fixture-only notification of original producer disposal.</summary>
[Alias(IRequestCqrsProbeGrainService.ServiceAlias), Version(1)]
internal interface IRequestCqrsProbeGrainService : IGrainService
{
    const string ServiceAlias = "keyload.request-cqrs-probe-grain-service.v1";
    const string LocateAlias = "locate-producer-claim";
    const string NotifyAlias = "notify-producer-disposed";

    /// <summary>Locates the original authenticated producer claim on this silo.</summary>
    [Alias(LocateAlias)]
    Task<RequestCqrsProbeNativeReply> LocateProducerClaimAsync(RequestCqrsProbeNativeRequest request, CancellationToken token);
    /// <summary>Joins notification of the actual original producer disposal.</summary>
    [Alias(NotifyAlias)]
    Task<RequestCqrsProbeNativeReply> NotifyProducerDisposedAsync(RequestCqrsProbeNativeRequest request, CancellationToken token);
}
