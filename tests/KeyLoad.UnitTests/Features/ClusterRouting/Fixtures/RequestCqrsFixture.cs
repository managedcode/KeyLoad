using KeyLoad.Core;
using KeyLoad.Orleans;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Communication.Orleans.Extensions;
using ManagedCode.Orleans.Graph.Extensions;
using ManagedCode.Orleans.Identity.Core.Serializations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Metadata;
using Orleans.Serialization;
using Orleans.TestingHost;
using IAsyncInitializer = TUnit.Core.Interfaces.IAsyncInitializer;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class RequestCqrsDataSourceAttribute(bool nativeTextMaintenance = false)
    : DataSourceGeneratorAttribute<RequestCqrsClusterFixture>
{
    public bool NativeTextMaintenance { get; } = nativeTextMaintenance;

    protected override IEnumerable<Func<RequestCqrsClusterFixture>> GenerateDataSources(DataGeneratorMetadata metadata)
    {
        if (NativeTextMaintenance)
        {
            yield return static () => new RequestCqrsClusterFixture(nativeMaintenance:
                static database => new KeyLoad.UnitTests.Features.Search.NativeTextMaintenanceTestRuntime(database));
            yield break;
        }
        yield return () => SharedDataSources.GetOrCreate<RequestCqrsClusterFixture>(
            SharedType.PerTestSession, metadata, null, static () => new RequestCqrsClusterFixture());
    }
}

internal sealed class RequestCqrsClusterFixture : IAsyncInitializer, IAsyncDisposable
{
    private const int ShutdownSeconds = 30;
    private const int StartupSeconds = 30;
    private const string RequestWorkNotJoined = "The fixture request work has not joined.";
    private readonly System.Threading.Lock disposalGate = new();
    private Task? disposal;
    private readonly Func<TestDatabase, KeyLoad.UnitTests.Features.Search.NativeTextMaintenanceTestRuntime>? nativeFactory;
    private bool clusterStopped;
    private NativeRequestWorkOwner? requestWork;
    private readonly string ownerId = Guid.NewGuid().ToString("N");

    internal RequestCqrsClusterFixture(TimeProvider? clock = null, GrainRoutingOptions? routing = null,
        IGrainRequestPhaseObserver? observer = null,
        Func<TestDatabase, KeyLoad.UnitTests.Features.Search.NativeTextMaintenanceTestRuntime>? nativeMaintenance = null)
    {
        nativeFactory = nativeMaintenance;
        Clock = clock ?? TimeProvider.System;
        RoutingOptions = UnitRoutingOptions.Routing(routing);
        Database = new TestDatabase(timeProvider: Clock, nativeReplicaAdmission: nativeFactory is not null);
        try
        {
            RequestCqrsFixtureOwners.Register(ownerId, this);
            var builder = new TestClusterBuilder(initialSilosCount: 1);
            builder.Properties[RequestCqrsFixtureOwners.ConfigurationKey] = ownerId;
            builder.AddSiloBuilderConfigurator<RequestCqrsSiloConfigurator>();
            builder.AddClientBuilderConfigurator<RequestCqrsClientConfigurator>();
            Cluster = builder.Build();
            Codec = new GrainRequestCodec(Database.Database, Clock, RoutingOptions) { PhaseObserver = observer };
        }
        catch (Exception startupFailure)
        {
            RequestCqrsFixtureOwners.Release(ownerId);
            try
            {
                Database.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(startupFailure, cleanupFailure);
            }

            throw;
        }
    }

    internal TestCluster Cluster { get; }
    internal Guid ConnectionId { get; } = Guid.NewGuid();
    internal NativeRuntimeTestOptions Timing { get; } = new();
    internal GrainRequestCodec Codec { get; }
    internal TestDatabase Database { get; }
    internal TimeProvider Clock { get; }
    internal IOptions<GrainRoutingOptions> RoutingOptions { get; }
    internal KeyLoad.UnitTests.Features.Search.NativeTextMaintenanceTestRuntime? NativeMaintenance { get; private set; }
    internal NativeRequestWorkOwner RequestWork => requestWork ??= new(RoutingOptions);

    public Task InitializeAsync() => InitializeAsync(CancellationToken.None);

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(StartupSeconds), TimeProvider.System);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        try
        {
            NativeMaintenance = nativeFactory?.Invoke(Database);
            await Cluster.DeployAsync(startup.Token);
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

    public ValueTask DisposeAsync()
    {
        var task = GetDisposalTask();
        GC.SuppressFinalize(this);
        return new(task);
    }

    private Task GetDisposalTask()
    {
        TaskCompletionSource registered;
        Task task;
        lock (disposalGate)
        {
            if (disposal is not null)
            {
                return disposal;
            }
            registered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            task = DisposeCoreAsync(registered.Task);
            disposal = task;
        }
        registered.TrySetResult();
        return task;
    }

    private async Task DisposeCoreAsync(Task registered)
    {
        await registered;
        var failures = new List<Exception>();
        await StopClusterAsync(failures);
        if (requestWork is { IsJoined: false })
        {
            KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            throw Errors.Fail(ErrorCode.OwnershipLost, RequestWorkNotJoined);
        }
        await DisposeOwnedResourcesAsync(failures);

        if (failures.Count == 1)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }

    }

    private async Task StopClusterAsync(List<Exception> failures)
    {
        if (requestWork is not null)
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(requestWork.DrainAsync, failures);
            if (!requestWork.IsJoined)
            {
                return;
            }
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(ShutdownSeconds), TimeProvider.System);
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(
            async () =>
            {
                await Cluster.StopAllSilosAsync(deadline.Token);
                clusterStopped = true;
            }, failures);
    }

    private async Task DisposeOwnedResourcesAsync(List<Exception> failures)
    {
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => Cluster.DisposeAsync().AsTask(), failures);
        if (requestWork is not null)
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => requestWork.DisposeAsync().AsTask(), failures);
        }
        if (NativeMaintenance is not null)
        {
            if (!clusterStopped)
            { return; }
            var before = failures.Count;
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => NativeMaintenance.DisposeAsync().AsTask(), failures);
            if (failures.Count != before)
            { return; }
        }
        try
        {
            KeyLoad.Server.ServerFailureObserver.Observe(Database.Dispose, failures);
        }
        finally
        {
            RequestCqrsFixtureOwners.Release(ownerId);
        }
    }
}

internal sealed class RequestCqrsSiloConfigurator : ISiloConfigurator
{
    public RequestCqrsSiloConfigurator()
    {
    }

    public void Configure(ISiloBuilder siloBuilder)
    {
        var fixture = RequestCqrsFixtureOwners.Resolve(siloBuilder.Configuration);
        siloBuilder.AddActivityPropagation();
        if (fixture.NativeMaintenance is { } native)
        {
            siloBuilder.Services.AddOptions<TextIndexMaintenanceOptions>()
                .Bind(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()
                    .GetSection(TextIndexMaintenanceOptions.SectionName))
                .Validate(options => options.IsValid(), TextIndexMaintenanceOptions.ValidationMessage)
                .ValidateOnStart();
            siloBuilder.Services.AddSingleton<INativeTextMaintenance>(native.Owner);
        }
        siloBuilder.Services.AddSerializer(serialization => serialization
            .AddAssembly(typeof(GrainRequestContextState).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
        siloBuilder.Services.AddSingleton(fixture.Database.Database);
        siloBuilder.Services.AddSingleton<ICommitCoordinator>(fixture.NativeMaintenance is not null
            ? new RequestCqrsNativeCommitCoordinator(fixture.Database)
            : new EmbeddedCoordinator(fixture.Database.Database));
        siloBuilder.Services.AddSingleton(fixture.Clock);
        siloBuilder.Services.AddSingleton(fixture.RoutingOptions);
        siloBuilder.Services.AddSingleton(UnitExecutionOptions.Messaging());
        siloBuilder.Services.AddSingleton(_ => fixture.RequestWork);
        siloBuilder.Services.AddSingleton(fixture.Codec);
        siloBuilder.Services.AddSingleton<NativeConnectionOwnerIdentity>();
        siloBuilder.Services.Configure<global::Orleans.Configuration.GrainCollectionOptions>(
            settings => settings.CollectionAge = fixture.Timing.OrdinaryCollectionAge);
        siloBuilder.Services.AddSingleton(new RequestCqrsCapabilityLedger());
        siloBuilder.Services.AddSingleton<IConfigureGrainTypeComponents>(services =>
            new RequestCqrsGrainComponentConfigurator(services.GetRequiredService<GrainClassMap>(), services));
        siloBuilder.AddOrleansGraph(configureGraph: graph =>
        {
            graph.AllowClientCallGrain<IRequestCqrsIdentityProbeGrain>();
            graph.AllowClientCallGrain<IManagementGrain>();
            graph.AllowClientCallGrain<IConnectionGrain>()
                .AddGrainTransition<IConnectionGrain, IConnectionGrain>()
                .MethodByName(nameof(IConnectionGrain.ExecuteStreamAsync), nameof(IConnectionGrain.ExecuteStreamAsync)).And()
                .AddGrainTransition<IConnectionGrain, ICommandPartitionGrain>()
                .MethodByName(nameof(IConnectionGrain.ExecuteStreamAsync), nameof(ICommandPartitionGrain.ExecuteAsync)).And();
        });
        siloBuilder.UseOrleansCommunication();
    }
}

internal sealed class RequestCqrsClientConfigurator : IClientBuilderConfigurator
{
    public RequestCqrsClientConfigurator()
    {
    }

    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
    {
        clientBuilder.AddActivityPropagation();
        clientBuilder.Services.AddSerializer(serialization => serialization
            .AddAssembly(typeof(GrainRequestContextState).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
        clientBuilder.AddOrleansGraph().UseOrleansCommunication();
    }
}
