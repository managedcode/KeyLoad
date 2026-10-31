using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Query;
using KeyLoad.Query.QueryExecution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class DistributedSearchReadCapability
{
    internal static Task<DistributedSearchPage> ExecuteAsync(DatabaseEngine database,
        IServiceProvider services, PrincipalRecord principal,
        DecodedGrainRequest request, Guid requestId, GrainRequestCodec codec, IGrainContext context,
        TimeProvider clock, IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var options = services.GetRequiredService<IOptions<QueryExecutionOptions>>();
        var execution = new DistributedSearchRemoteExecution(database, principal.Id, services, codec, request);
        Func<CancellationToken, Task>? observed = null;
        if (codec.HasPhaseObserver)
        {
            observed = new DistributedSearchStatisticsObservation(codec, request, requestId, context).ObserveAsync;
        }
        return DistributedSearchCoordinator.ExecuteBoundAsync(database,
            GrainPayload.Read<DistributedSearchRequest>(request.Payload), options, execution.PrepareAsync,
            services.GetRequiredService<Func<ICommitCoordinator>>()().ReadBarrierAsync,
            clock, observed, binding, token);
    }
}
