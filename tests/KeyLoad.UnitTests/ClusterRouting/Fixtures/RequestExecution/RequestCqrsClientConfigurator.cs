using KeyLoad.Clustering;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Communication.Orleans.Extensions;
using ManagedCode.Orleans.Graph.Extensions;
using ManagedCode.Orleans.Identity.Core.Serializations;
using Microsoft.Extensions.Configuration;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsClientConfigurator : IClientBuilderConfigurator
{
    public RequestCqrsClientConfigurator()
    {
    }

    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
    {
        clientBuilder.AddActivityPropagation();
        clientBuilder.Services.AddSerializer(serialization => serialization
            .AddAssembly(typeof(GrainRequestContextState).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
        clientBuilder.AddOrleansGraph().UseOrleansCommunication();
    }
}
