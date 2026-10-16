using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

internal static class DistributedSearchReadCapability
{
    private const string NotConfigured = "The distributed canonical search owner route is not configured.";

    internal static Task<DistributedSearchPageV1> ExecuteAsync(IServiceProvider services, PrincipalRecord principal,
        DecodedGrainRequest request, Guid requestId, GrainRequestCodec codec, IGrainContext context,
        CancellationToken token, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var router = services.GetService<IRemoteDistributedSearchRouter>()
            ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, NotConfigured);
        Func<CancellationToken, Task>? observed = null;
        if (codec.HasPhaseObserver)
        {
            observed = new DistributedSearchStatisticsObservation(codec, request, requestId, context).ObserveAsync;
        }
        return router.ReadAsync(request.Envelope, principal,
            GrainNativePayload.Read<DistributedSearchRequestV1>(request.Payload), token, observed, binding);
    }
}
