#pragma warning disable ORLEANSEXP005
using KeyLoad.Clustering;
using KeyLoad.Server;
using ManagedCode.Communication.CQRS;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Communication.Orleans.Extensions;
using ManagedCode.Orleans.Graph.Extensions;
using ManagedCode.Orleans.Identity.Core.Serializations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Journaling;
using Orleans.Metadata;
using Orleans.Providers;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class RuntimeJournalSiloConfigurator : ISiloConfigurator
{
    public RuntimeJournalSiloConfigurator()
    {
    }

    public void Configure(ISiloBuilder siloBuilder)
    {
        var fixture = RuntimeRuntimeJournalFixture.Current;
        var routing = UnitRoutingOptions.Routing();
        siloBuilder.Services.AddSerializer(serialization => serialization
            .AddAssembly(typeof(RuntimeJournalReplayGrain).Assembly)
            .AddAssembly(typeof(GrainRequestContextState).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
        siloBuilder.Services.AddSingleton(fixture.Database.Database);
        var coordinator = new EmbeddedCoordinator(fixture.Database.Database);
        siloBuilder.Services.AddSingleton<Func<ICommitCoordinator>>(() => coordinator);
        siloBuilder.Services.AddSingleton(TimeProvider.System);
        var requestWork = fixture.RequestWork;
        siloBuilder.Services.AddSingleton<Func<RequestWorkOwner>>(() => requestWork);
        siloBuilder.Services.AddSingleton(fixture.Codec);
        siloBuilder.Services.AddSingleton<ConnectionOwnerIdentity>();
        siloBuilder.Services.AddSingleton(routing);
        siloBuilder.Services.AddSingleton(fixture.JournalOptions);
        siloBuilder.Services.AddOptions<RuntimeTestOptions>()
            .Validate(options => options.IsValid(), "The execution runtime test timing profile is invalid.")
            .ValidateOnStart();
        siloBuilder.Services.AddSingleton(services => new RuntimeJournalClient(
            services.GetRequiredService<IGrainFactory>(), () => fixture.Codec, fixture.Database.Database.OperationLimitsOptions,
            services,
            services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
            services.GetRequiredService<IOptions<GrainRoutingOptions>>(), fixture.JournalOptions,
            services.GetRequiredService<RuntimeJournalAdmission>()));
        siloBuilder.Services.AddSingleton<RuntimeJournalAdmission>();
        siloBuilder.Services.AddSingleton<IConfigureGrainTypeComponents>(services =>
            new RequestCqrsGrainComponentConfigurator(services.GetRequiredService<GrainClassMap>(), services));
        siloBuilder.Services.AddSingleton<IConfigureGrainTypeComponents>(services =>
            new RuntimeJournalReplayActivatorConfiguration(services.GetRequiredService<GrainClassMap>(), services));
        siloBuilder.Services.Configure<JournaledStateManagerOptions>(options =>
            options.JournalFormatKey = RuntimeJournalStoragePolicy.BinaryFormat);
        siloBuilder.Configure<GrainTypeOptions>(options =>
        {
            options.AddClass(typeof(RuntimeJournalReplayGrain));
            options.AddClass(typeof(RuntimeJournalGraphCallerProbeGrain));
        });
        siloBuilder.AddJournalStorage<RuntimeJournalStorageProvider>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME,
            services => ActivatorUtilities.CreateInstance<RuntimeJournalStorageProvider>(services));
        ConfigureGraph(siloBuilder);
        JobGraphRegistration.ExtendJournalStorage(siloBuilder.Services);
        siloBuilder.UseOrleansCommunication();
    }

    private static void ConfigureGraph(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddOrleansGraph(configureGraph: graph =>
        {
            graph.AllowClientCallGrain<IConnectionGrain>()
                .AllowClientCallGrain<IRuntimeJournalReplayGrain>()
                .AllowClientCallGrain<IRuntimeJournalGraphCallerProbeGrain>()
                .AddGrainTransition<IConnectionGrain, ICommandPartitionGrain>()
                .MethodByName(nameof(IConnectionGrain.ExecuteStreamAsync), nameof(ICommandPartitionGrain.ExecuteAsync)).And();
        });
    }
}

#pragma warning restore ORLEANSEXP005
