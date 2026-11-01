using ManagedCode.Communication.Orleans.Extensions;
using Microsoft.Extensions.Configuration;
using Orleans.TestingHost;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class CqrsClientConfigurator : IClientBuilderConfigurator
{
    public CqrsClientConfigurator()
    {
    }

    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
    {
        clientBuilder
            .UseOrleansCommunication();
    }
}
