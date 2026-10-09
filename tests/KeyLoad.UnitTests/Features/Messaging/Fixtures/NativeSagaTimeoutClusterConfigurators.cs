using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Communication.Orleans.Extensions;
using ManagedCode.Orleans.Graph.Extensions;
using ManagedCode.Orleans.Identity.Core.Serializations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class NativeSagaTimeoutSiloConfigurator : ISiloConfigurator
{
    public NativeSagaTimeoutSiloConfigurator() { }

    public void Configure(ISiloBuilder siloBuilder)
    {
        var fixture = NativeSagaTimeoutFixture.Current;
        siloBuilder.Services.AddSerializer(serialization => serialization
            .AddAssembly(typeof(GrainRequestContextState).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
        siloBuilder.Services.AddSingleton(fixture.Database.Database);
        siloBuilder.Services.AddSingleton(fixture.Coordinator);
        siloBuilder.Services.AddSingleton<ICommitCoordinator>(fixture.Coordinator);
        siloBuilder.Services.AddSingleton(TimeProvider.System);
        siloBuilder.Services.AddSingleton(fixture.RequestWork);
        siloBuilder.Services.AddSingleton(fixture.Codec);
        siloBuilder.Services.AddSingleton<NativeConnectionOwnerIdentity>();
        siloBuilder.Services.AddSingleton(fixture.Routing);
        siloBuilder.Services.AddSingleton(fixture.DurableJobOptions);
        siloBuilder.Services.AddSingleton(fixture.JournalOptions);
        siloBuilder.Services.AddSingleton(fixture.DueOptions);
        siloBuilder.AddOrleansGraph(configureGraph: graph => graph
            .AllowClientCallGrain<IConnectionGrain>()
            .AllowClientCallGrain<IRecurringDueCoordinatorGrain>()
            .AddGrainTransition<IRecurringDueCoordinatorGrain, IConnectionGrain>()
            .MethodByName(nameof(IRecurringDueCoordinatorGrain.ProcessDueAsync), nameof(IConnectionGrain.ExecuteStreamAsync)).And()
            .AddGrainTransition<IConnectionGrain, ICommandPartitionGrain>()
            .MethodByName(nameof(IConnectionGrain.ExecuteStreamAsync), nameof(ICommandPartitionGrain.ExecuteAsync)).And());
        siloBuilder.UseOrleansCommunication();
        NativeRuntimeJournalRegistration.Register(siloBuilder, fixture.JournalOptions, fixture.DurableJobOptions);
    }
}

internal sealed class NativeSagaTimeoutClientConfigurator : IClientBuilderConfigurator
{
    public NativeSagaTimeoutClientConfigurator() { }

    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
    {
        clientBuilder.Services.AddSerializer(serialization => serialization
            .AddAssembly(typeof(GrainRequestContextState).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
        clientBuilder.AddOrleansGraph().UseOrleansCommunication();
    }
}
