#pragma warning disable ORLEANSEXP005
using KeyLoad.Core;
using KeyLoad.Orleans;
using ManagedCode.Communication.CQRS;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Communication.Orleans.Extensions;
using ManagedCode.Orleans.Graph.Extensions;
using ManagedCode.Orleans.Identity.Core.Serializations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
    private const int StartupSeconds = 30;
    private const int ShutdownSeconds = 30;
    private static RuntimeJournalNativeFixture? active;
    internal readonly TestDatabase Database = new();
    internal readonly IOptions<RuntimeJournalOptions> JournalOptions = Options.Create(new RuntimeJournalOptions());
    private NativeRequestWorkOwner? requestWork;

    internal RuntimeJournalNativeFixture()
    {
        Database.Database.ConfigureRuntimeJournal(JournalOptions);
        active = this;
        try
        {
            var builder = new TestClusterBuilder(initialSilosCount: 1);
            builder.AddSiloBuilderConfigurator<RuntimeJournalNativeSiloConfigurator>();
            builder.AddClientBuilderConfigurator<RuntimeJournalNativeClientConfigurator>();
            Cluster = builder.Build();
            Codec = new(Database.Database, TimeProvider.System, UnitRoutingOptions.Routing());
        }
        catch
        {
            active = null;
            Database.Dispose();
            throw;
        }
    }

    internal TestCluster Cluster { get; }
    internal GrainRequestCodec Codec { get; }
    internal NativeRequestWorkOwner RequestWork => requestWork ??= new(UnitRoutingOptions.Routing());

    internal static RuntimeJournalNativeFixture Current
        => active ?? throw new InvalidOperationException("The native runtime journal fixture is not active.");

    internal IServiceProvider SiloServices => Cluster.GetSiloServiceProvider();
    internal IJournalStorageProvider Provider => SiloServices.GetRequiredService<IJournalStorageProvider>();
    internal IJournalStorageCatalog Catalog => SiloServices.GetRequiredService<IJournalStorageCatalog>();

    public async Task InitializeAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(StartupSeconds));
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
        var failures = new List<Exception>();
        if (requestWork is not null)
        {
            await ObserveAsync(requestWork.DrainAsync, failures);
        }
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(ShutdownSeconds)))
        {
            await ObserveAsync(() => Cluster.StopAllSilosAsync(deadline.Token), failures);
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
        catch (Exception error)
        {
            failures.Add(error);
        }
        active = null;
        if (failures.Count == 1) throw failures[0];
        if (failures.Count > 1) throw new AggregateException(failures);
    }

    private async Task BootstrapAsync(CancellationToken cancellationToken)
    {
        var principal = Database.Store.Read(view => Database.Database.Principal(view, "root", TimeProvider.System.GetUtcNow()));
        var mutation = new RuntimeJournalMutation(RuntimeJournalAction.BootstrapIdentity, string.Empty, Guid.Empty,
            0, 0, null, ReadOnlyMemory<byte>.Empty, new(StringComparer.Ordinal), []);
        var requestId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var signed = Codec.CreateRuntimeJournalCommand(requestId, principal.Id, commandId,
            NativeSerialization.Serialize(mutation));
        using var identity = new GrainRequestIdentityScope(Cluster.ServiceProvider, principal, requestId,
            commandId, cancellationToken);
        var actor = Cluster.Client.GetGrain<IRequestGrain>(requestId);
        var serializer = Cluster.ServiceProvider.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>();
        var reply = await GrainRequestStreamConsumer.DrainAsync(
            streamToken => actor.ExecuteStreamAsync(signed, streamToken), serializer, requestId,
            TimeProvider.System, cancellationToken, UnitRoutingOptions.Routing()).ConfigureAwait(false);
        if (reply.Error is not null)
        {
            throw Errors.Fail(reply.Error.Value, reply.SafeDetail ?? "Runtime journal bootstrap failed.");
        }
    }

    private static async Task ObserveAsync(Func<Task> operation, ICollection<Exception> failures)
    {
        try { await operation().ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
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
            .AddAssembly(typeof(GrainRequestContextState).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
        siloBuilder.Services.AddSingleton(fixture.Database.Database);
        siloBuilder.Services.AddSingleton<ICommitCoordinator>(new EmbeddedCoordinator(fixture.Database.Database));
        siloBuilder.Services.AddSingleton(TimeProvider.System);
        siloBuilder.Services.AddSingleton(_ => fixture.RequestWork);
        siloBuilder.Services.AddSingleton(fixture.Codec);
        siloBuilder.Services.AddSingleton(routing);
        siloBuilder.Services.AddSingleton(fixture.JournalOptions);
        siloBuilder.Services.AddSingleton<RuntimeJournalAdmission>();
        siloBuilder.Services.AddSingleton<IConfigureGrainTypeComponents>(services =>
            new RequestCqrsGrainComponentConfigurator(services.GetRequiredService<GrainClassMap>(), services));
        siloBuilder.Services.Configure<JournaledStateManagerOptions>(options =>
            options.JournalFormatKey = RuntimeJournalStoragePolicy.BinaryFormat);
        siloBuilder.AddJournalStorage<RuntimeJournalStorageProvider>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME,
            services => ActivatorUtilities.CreateInstance<RuntimeJournalStorageProvider>(services));
        siloBuilder.AddOrleansGraph(configureGraph: graph =>
        {
            graph.AllowClientCallGrain<IRequestGrain>()
                .AddGrainTransition<IRequestGrain, IDatabaseReadGrain>()
                .MethodByName(nameof(IRequestGrain.ExecuteStreamAsync), nameof(IDatabaseReadGrain.ExecuteAsync)).And()
                .AddGrainTransition<IRequestGrain, ICommandPartitionGrain>()
                .MethodByName(nameof(IRequestGrain.ExecuteStreamAsync), nameof(ICommandPartitionGrain.ExecuteAsync)).And();
        });
        siloBuilder.UseOrleansCommunication();
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
        clientBuilder.AddOrleansGraph().UseOrleansCommunication();
    }
}
#pragma warning restore ORLEANSEXP005
