using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Query;
using KeyLoad.Query.QueryExecution;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

internal static class PartitionQueryReadCapability
{
    internal static Task<PartitionQueryPage> ExecuteAsync(DatabaseEngine database,
        IServiceProvider services, PrincipalRecord principal, DecodedGrainRequest request,
        GrainRequestCodec codec, TimeProvider clock, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: BCL null validation accepts the binding IResourceLifetimeWorkBinding reference through its object parameter. */;
        var execution = new PartitionQueryLeafDispatch(database, principal.Id, services, codec, request);
        return PartitionQueryRemoteExecution.ExecuteBoundAsync(services.GetRequiredService<QueryEngine>(),
            (GrainPayload.ReadPublicInput<PartitionQueryRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.PartitionQueryRequest at this closed operation boundary before executing typed code. */), execution.PrepareAsync,
            services.GetRequiredService<Func<ICommitCoordinator>>()().ReadBarrierAsync,
            clock, binding, token);
    }
}
