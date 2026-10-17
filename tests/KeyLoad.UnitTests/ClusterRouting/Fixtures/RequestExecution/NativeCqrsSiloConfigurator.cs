using ManagedCode.Communication.Orleans.Extensions;
using ManagedCode.Orleans.Graph.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Metadata;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeCqrsSiloConfigurator : ISiloConfigurator
{
    public NativeCqrsSiloConfigurator()
    {
    }

    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.Services.AddSingleton<IConfigureGrainTypeComponents>(services =>
            new NativeCqrsGrainComponentConfigurator(
                services.GetRequiredService<GrainClassMap>(),
                services));
        siloBuilder
            .AddOrleansGraph()
            .UseOrleansCommunication();
    }
}
