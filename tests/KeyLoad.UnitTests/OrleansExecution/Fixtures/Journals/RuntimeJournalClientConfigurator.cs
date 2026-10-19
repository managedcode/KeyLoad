using KeyLoad.Clustering;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Communication.Orleans.Extensions;
using ManagedCode.Orleans.Graph.Extensions;
using ManagedCode.Orleans.Identity.Core.Serializations;
using Microsoft.Extensions.Configuration;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class RuntimeJournalClientConfigurator : IClientBuilderConfigurator
{
    public RuntimeJournalClientConfigurator()
    {
    }

    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
    {
        clientBuilder.Services.AddSerializer(serialization => serialization
            .AddAssembly(typeof(GrainRequestContextState).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
        clientBuilder.AddOrleansGraph(configureGraph: graph =>
            graph.AllowClientCallGrain<IRuntimeJournalReplayGrain>()
                .AllowClientCallGrain<IRuntimeJournalGraphCallerProbeGrain>()).UseOrleansCommunication();
    }
}
