using KeyLoad.Clustering;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Communication.Orleans.Extensions;
using ManagedCode.Orleans.Graph.Extensions;
using ManagedCode.Orleans.Identity.Core.Serializations;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsHostedSerialization
{
    internal static void Silo(ISiloBuilder silo)
    {
        silo.AddActivityPropagation();
        silo.Services.AddSerializer(serialization => serialization
            .AddAssembly(typeof(GrainRequestContextState).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
        silo.AddOrleansGraph(graph =>
        {
            graph.AllowClientCallGrain<IRequestCqrsIdentityProbeGrain>();
            graph.AllowClientCallGrain<IManagementGrain>();
            graph.AllowClientCallGrain<IConnectionGrain>()
                .AddGrainTransition<IConnectionGrain, IConnectionGrain>()
                .MethodByName(nameof(IConnectionGrain.ExecuteStreamAsync), nameof(IConnectionGrain.ExecuteStreamAsync)).And()
                .AddGrainTransition<IConnectionGrain, ICommandPartitionGrain>()
                .MethodByName(nameof(IConnectionGrain.ExecuteStreamAsync), nameof(ICommandPartitionGrain.ExecuteAsync)).And();
        });
        silo.UseOrleansCommunication();
    }
}
