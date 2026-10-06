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
internal sealed class RequestCqrsDataSourceAttribute : DataSourceGeneratorAttribute<RequestCqrsClusterFixture>
{
    public RequestCqrsDataSourceAttribute()
    {
    }

    protected override IEnumerable<Func<RequestCqrsClusterFixture>> GenerateDataSources(DataGeneratorMetadata metadata)
    {
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
    private NativeRequestWorkOwner? requestWork;
    private static RequestCqrsClusterFixture? ActiveFixture { get; set; }

    internal RequestCqrsClusterFixture()
    {
        Database = new TestDatabase();
        ActiveFixture = this;
        try
        {
            var builder = new TestClusterBuilder(initialSilosCount: 1);
            builder.AddSiloBuilderConfigurator<RequestCqrsSiloConfigurator>();
            builder.AddClientBuilderConfigurator<RequestCqrsClientConfigurator>();
            Cluster = builder.Build();
            Codec = new GrainRequestCodec(Database.Database, TimeProvider.System, RoutingOptions);
        }
        catch (Exception startupFailure)
        {
            ActiveFixture = null;
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

    internal static RequestCqrsClusterFixture Current
        => ActiveFixture ?? throw new InvalidOperationException("The native request fixture is not active.");

    internal TestCluster Cluster { get; }
    internal GrainRequestCodec Codec { get; }
    internal TestDatabase Database { get; }
    internal IOptions<GrainRoutingOptions> RoutingOptions { get; } = UnitRoutingOptions.Routing();
    internal NativeRequestWorkOwner RequestWork => requestWork ??= new(RoutingOptions);

    public async Task InitializeAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(StartupSeconds));
        try
        {
            await Cluster.DeployAsync(deadline.Token);
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
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(ShutdownSeconds));
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(
            () => Cluster.StopAllSilosAsync(deadline.Token), failures);
    }

    private async Task DisposeOwnedResourcesAsync(List<Exception> failures)
    {
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => Cluster.DisposeAsync().AsTask(), failures);
        if (requestWork is not null)
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => requestWork.DisposeAsync().AsTask(), failures);
        }
        try
        {
            KeyLoad.Server.ServerFailureObserver.Observe(Database.Dispose, failures);
        }
        finally
        {
            ActiveFixture = null;
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
        var fixture = RequestCqrsClusterFixture.Current;
        siloBuilder.AddActivityPropagation();
        siloBuilder.Services.AddSerializer(serialization => serialization
            .AddAssembly(typeof(GrainRequestContextState).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
        siloBuilder.Services.AddSingleton(fixture.Database.Database);
        siloBuilder.Services.AddSingleton<ICommitCoordinator>(new EmbeddedCoordinator(fixture.Database.Database));
        siloBuilder.Services.AddSingleton(TimeProvider.System);
        siloBuilder.Services.AddSingleton(fixture.RoutingOptions);
        siloBuilder.Services.AddSingleton(_ => fixture.RequestWork);
        siloBuilder.Services.AddSingleton<GrainRequestCodec>();
        siloBuilder.Services.AddSingleton(new RequestCqrsCapabilityLedger());
        siloBuilder.Services.AddSingleton<IConfigureGrainTypeComponents>(services =>
            new RequestCqrsGrainComponentConfigurator(services.GetRequiredService<GrainClassMap>(), services));
        siloBuilder.AddOrleansGraph(configureGraph: graph =>
        {
            graph.AllowClientCallGrain<IRequestCqrsIdentityProbeGrain>();
            graph.AllowClientCallGrain<IRequestGrain>()
                .AddGrainTransition<IRequestGrain, IDatabaseReadGrain>()
                .MethodByName(nameof(IRequestGrain.ExecuteStreamAsync), nameof(IDatabaseReadGrain.ExecuteAsync)).And()
                .AddGrainTransition<IRequestGrain, ICommandPartitionGrain>()
                .MethodByName(nameof(IRequestGrain.ExecuteStreamAsync), nameof(ICommandPartitionGrain.ExecuteAsync)).And();
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
        clientBuilder.Services.AddSerializer(serialization => serialization
            .AddAssembly(typeof(GrainRequestContextState).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
        clientBuilder.AddOrleansGraph().UseOrleansCommunication();
    }
}
