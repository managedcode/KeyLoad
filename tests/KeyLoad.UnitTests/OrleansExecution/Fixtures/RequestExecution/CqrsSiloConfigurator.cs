using ManagedCode.Communication.Orleans.Extensions;
using ManagedCode.Orleans.Graph.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Metadata;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class CqrsSiloConfigurator : ISiloConfigurator
{
    public CqrsSiloConfigurator()
    {
    }

    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.Services.AddSingleton<IConfigureGrainTypeComponents>(services =>
            new CqrsGrainComponentConfigurator(
                services.GetRequiredService<GrainClassMap>(),
                services));
        siloBuilder
            .AddOrleansGraph()
            .UseOrleansCommunication();
    }
}
