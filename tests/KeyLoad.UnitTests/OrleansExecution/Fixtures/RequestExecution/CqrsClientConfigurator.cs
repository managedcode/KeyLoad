using ManagedCode.Communication.Orleans.Extensions;
using ManagedCode.Orleans.Graph.Extensions;
using Microsoft.Extensions.Configuration;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class CqrsClientConfigurator : IClientBuilderConfigurator
{
    public CqrsClientConfigurator()
    {
    }

    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
    {
        clientBuilder
            .AddOrleansGraph()
            .UseOrleansCommunication();
    }
}
