using KeyLoad.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class ResourceOwnerInventoryObservedReadExecution
{
    internal static async Task<ResourceOwnerInventoryReadReplyV1> ExecuteAsync(DatabaseEngine database,
        IServiceProvider services, TimeProvider clock, PrincipalRecord principal, DecodedGrainRequest request,
        GrainRequestCodec codec, IGrainContext context, CancellationToken token)
    {
        var query = GrainNativePayload.Read<ResourceOwnerInventoryReadRequestV1>(request.Payload);
        if (codec.HasPhaseObserver && query.Operation == ResourceOwnerInventoryReadOperation.ValidateOwner)
        {
            await codec.ObservePhaseAsync(request, GrainRequestPhase.ResourceOwnerInventoryOwnerValidation,
                context, token).ConfigureAwait(true);
            ConnectionReadExecution.ValidateFreshRequest(codec, request, request.Envelope.RequestId, token);
            principal = GrainRequestAuthority.ReloadForRequest(database, request.Envelope, clock);
        }
        return ResourceOwnerInventoryReadExecution.Execute(database, principal, query,
            services.GetRequiredService<SiloStorageIdentityProvider>(),
            services.GetRequiredService<IOptions<DatabaseLimits>>(), clock, token);
    }
}
