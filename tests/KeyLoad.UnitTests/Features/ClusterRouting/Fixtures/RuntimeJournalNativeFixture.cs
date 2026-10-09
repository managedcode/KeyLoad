#pragma warning disable ORLEANSEXP005
using System.Diagnostics.CodeAnalysis;
using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server;
using ManagedCode.Communication.CQRS;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Communication.Orleans.Extensions;
using ManagedCode.Orleans.Graph.Extensions;
using ManagedCode.Orleans.Identity.Core.Serializations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Journaling;
using Orleans.Metadata;
using Orleans.Providers;
using Orleans.Serialization;
using Orleans.TestingHost;
using IAsyncInitializer = TUnit.Core.Interfaces.IAsyncInitializer;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class RuntimeJournalNativeDataSourceAttribute : DataSourceGeneratorAttribute<RuntimeJournalNativeFixture>
{
    protected override IEnumerable<Func<RuntimeJournalNativeFixture>> GenerateDataSources(DataGeneratorMetadata metadata)
    {
        yield return () => SharedDataSources.GetOrCreate<RuntimeJournalNativeFixture>(
            SharedType.PerTestSession, metadata, null, static () => new RuntimeJournalNativeFixture());
    }
}
internal sealed class RuntimeJournalNativeFixture : IAsyncInitializer, IAsyncDisposable
{
    private int disposed;
    internal readonly TestDatabase Database = new();
    internal readonly IOptions<RuntimeJournalOptions> JournalOptions = Options.Create(new RuntimeJournalOptions());
    internal readonly IOptions<NativeRuntimeTestOptions> TimingOptions = Options.Create(new NativeRuntimeTestOptions());
    private NativeRequestWorkOwner? requestWork;

    internal RuntimeJournalNativeFixture()
    {
        if (!TimingOptions.Value.IsValid())
        {
            throw new InvalidOperationException("The native runtime test timing profile is invalid.");
        }
        Database.Database.ConfigureRuntimeJournal(JournalOptions);
        Current = this;
        try
        {
            var builder = new TestClusterBuilder(initialSilosCount: 1);
            builder.AddSiloBuilderConfigurator<RuntimeJournalNativeSiloConfigurator>();
            builder.AddClientBuilderConfigurator<RuntimeJournalNativeClientConfigurator>();
            Cluster = builder.Build();
            Codec = new(Database.Database, TimeProvider.System, UnitRoutingOptions.Routing());
        }
        catch (Exception failure)
        {
            Current = null;
            try
            {
                Database.Dispose();
            }
            catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                throw new AggregateException(failure, cleanup);
            }
            catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                throw new AggregateException(failure, cleanup);
            }

            throw;
        }
    }

    internal TestCluster Cluster { get; }
    internal GrainRequestCodec Codec { get; }
    internal NativeRequestWorkOwner RequestWork => requestWork ??= new(UnitRoutingOptions.Routing());

    [AllowNull]
    internal static RuntimeJournalNativeFixture Current { get => field ?? throw new InvalidOperationException("The native runtime journal fixture is not active."); private set; }

    internal IServiceProvider SiloServices => Cluster.GetSiloServiceProvider();
    internal IJournalStorageProvider Provider => SiloServices.GetRequiredService<IJournalStorageProvider>();
    internal IJournalStorageCatalog Catalog => SiloServices.GetRequiredService<IJournalStorageCatalog>();

    public async Task InitializeAsync()
    {
        using var deadline = new CancellationTokenSource(TimingOptions.Value.StartupTimeout, TimeProvider.System);
        try
        {
            await Cluster.DeployAsync(deadline.Token);
            await BootstrapAsync(deadline.Token);
            SiloServices.GetRequiredService<RuntimeJournalAdmission>().Open(deadline.Token);
        }
        catch (Exception startupFailure)
        {
            try
            {
                await DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(startupFailure, cleanupFailure);
            }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        var failures = new List<Exception>();
        using (var deadline = new CancellationTokenSource(TimingOptions.Value.ShutdownTimeout, TimeProvider.System))
        {
            await ObserveAsync(() => Cluster.StopAllSilosAsync(deadline.Token), failures);
        }
        if (requestWork is not null)
        {
            await ObserveAsync(requestWork.DrainAsync, failures);
        }
        await ObserveAsync(() => Cluster.DisposeAsync().AsTask(), failures);
        if (requestWork is not null)
        {
            await ObserveAsync(() => requestWork.DisposeAsync().AsTask(), failures);
        }
        try
        {
            Database.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        Current = null;
        if (failures.Count == 1)
        {
            throw failures[0];
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }
    }

    private async Task BootstrapAsync(CancellationToken cancellationToken)
    {
        var principal = Database.Store.Read(view => Database.Database.Principal(view, "root", TimeProvider.System.GetUtcNow()));
        var services = SiloServices;
        var startup = new RuntimeJournalStartupRequests(services.GetRequiredService<IGrainFactory>(), Codec,
            Database.Database, services.GetRequiredService<ICommitCoordinator>(), services, TimeProvider.System,
            UnitRoutingOptions.Routing());
        await startup.BootstrapAsync(principal, Guid.NewGuid(), cancellationToken).ConfigureAwait(false);
    }

    private static async Task ObserveAsync(Func<Task> operation, List<Exception> failures)
    {
        try
        { await operation().ConfigureAwait(false); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
    }
}

internal sealed class RuntimeJournalNativeSiloConfigurator : ISiloConfigurator
{
    public RuntimeJournalNativeSiloConfigurator() { }

    public void Configure(ISiloBuilder siloBuilder)
    {
        var fixture = RuntimeJournalNativeFixture.Current;
        var routing = UnitRoutingOptions.Routing();
        siloBuilder.Services.AddSerializer(serialization => serialization
            .AddAssembly(typeof(RuntimeJournalReplayGrain).Assembly)
            .AddAssembly(typeof(GrainRequestContextState).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
        siloBuilder.Services.AddSingleton(fixture.Database.Database);
        siloBuilder.Services.AddSingleton<ICommitCoordinator>(new EmbeddedCoordinator(fixture.Database.Database));
        siloBuilder.Services.AddSingleton(TimeProvider.System);
        siloBuilder.Services.AddSingleton(_ => fixture.RequestWork);
        siloBuilder.Services.AddSingleton(fixture.Codec);
        siloBuilder.Services.AddSingleton<NativeConnectionOwnerIdentity>();
        siloBuilder.Services.AddSingleton(routing);
        siloBuilder.Services.AddSingleton(fixture.JournalOptions);
        siloBuilder.Services.AddOptions<NativeRuntimeTestOptions>()
            .Validate(options => options.IsValid(), "The native runtime test timing profile is invalid.")
            .ValidateOnStart();
        siloBuilder.Services.AddSingleton(services => new RuntimeJournalClient(
            services.GetRequiredService<IGrainFactory>(), fixture.Codec, fixture.Database.Database,
            services.GetRequiredService<ICommitCoordinator>(), services,
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
        NativeJobGraphRegistration.Extend(siloBuilder.Services);
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

internal sealed class RuntimeJournalNativeClientConfigurator : IClientBuilderConfigurator
{
    public RuntimeJournalNativeClientConfigurator() { }

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
#pragma warning restore ORLEANSEXP005
