using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Communication.Orleans.Extensions;
using ManagedCode.Orleans.Graph.Extensions;
using ManagedCode.Orleans.Identity.Core.Serializations;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class SagaTimeoutSiloConfigurator : ISiloConfigurator
{
    public SagaTimeoutSiloConfigurator()
    {
    }

    public void Configure(ISiloBuilder siloBuilder)
    {
        var fixture = SagaTimeoutFixture.Current;
        var coordinator = fixture.Coordinator;
        var requestWork = fixture.RequestWork;
        siloBuilder.Services.AddSerializer(serialization => serialization
            .AddAssembly(typeof(GrainRequestContextState).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
        siloBuilder.Services.AddSingleton(fixture.Database.Database);
        siloBuilder.Services.AddSingleton<Func<ICommitCoordinator>>(() => coordinator);
        siloBuilder.Services.AddSingleton(TimeProvider.System);
        siloBuilder.Services.AddSingleton<Func<RequestWorkOwner>>(() => requestWork);
        siloBuilder.Services.AddSingleton(fixture.Codec);
        siloBuilder.Services.AddSingleton<ConnectionOwnerIdentity>();
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
        RuntimeJournalRegistration.Register(siloBuilder);
    }
}
