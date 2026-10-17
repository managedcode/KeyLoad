using ManagedCode.Communication.Orleans.Extensions;
using ManagedCode.Orleans.Graph.Extensions;
using Microsoft.Extensions.Configuration;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeCqrsClientConfigurator : IClientBuilderConfigurator
{
    public NativeCqrsClientConfigurator()
    {
    }

    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
    {
        clientBuilder
            .AddOrleansGraph()
            .UseOrleansCommunication();
    }
}
