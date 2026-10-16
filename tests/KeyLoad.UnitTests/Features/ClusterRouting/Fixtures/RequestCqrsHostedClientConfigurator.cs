using KeyLoad.Clustering;
using KeyLoad.UnitTests.Features.ClusterReplication;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Communication.Orleans.Extensions;
using ManagedCode.Orleans.Graph.Extensions;
using ManagedCode.Orleans.Identity.Core.Serializations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsHostedClientConfigurator : IClientBuilderConfigurator
{
    public void Configure(IConfiguration configuration, IClientBuilder client)
    {
        var profile = RequestCqrsCohortOwners.Resolve(configuration).Hosted
            ?? throw new InvalidOperationException(RequestCqrsHostedProtocol.ClientAbsent);
        client.AddActivityPropagation();
        client.Services.AddSingleton(profile.Routing);
        client.Services.AddSingleton<IPhysicalRequestPlacement>(_ => profile.Placement);
        client.Services.AddSerializer(serialization => serialization
            .AddAssembly(typeof(GrainRequestContextState).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
        client.AddOrleansGraph().UseOrleansCommunication();
    }
}
