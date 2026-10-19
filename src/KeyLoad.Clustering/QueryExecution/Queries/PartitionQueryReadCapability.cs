using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

internal static class PartitionQueryReadCapability
{
    internal static Task<PartitionQueryPage> ExecuteAsync(DatabaseEngine database,
        IServiceProvider services, PrincipalRecord principal, DecodedGrainRequest request,
        GrainRequestCodec codec, TimeProvider clock, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var execution = new PartitionQueryLeafDispatch(database, principal.Id, services, codec, request);
        return PartitionQueryRemoteExecution.ExecuteBoundAsync(services.GetRequiredService<QueryEngine>(),
            GrainPayload.ReadPublicInput<PartitionQueryRequest>(request.Payload), execution.PrepareAsync,
            services.GetRequiredService<Func<ICommitCoordinator>>()().ReadBarrierAsync,
            clock, request.Envelope.ExpiresAt, binding, token);
    }
}
