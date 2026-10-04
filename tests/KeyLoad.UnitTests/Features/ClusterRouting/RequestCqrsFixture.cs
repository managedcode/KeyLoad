using KeyLoad.Core;
using KeyLoad.Orleans;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Communication.Orleans.Extensions;
using ManagedCode.Orleans.Graph.Extensions;
using ManagedCode.Orleans.Identity.Core.Serializations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    private bool disposed;
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
            Codec = new GrainRequestCodec(Database.Database, TimeProvider.System);
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

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        var failures = new List<Exception>();
        await StopClusterAsync(failures);
        await DisposeOwnedResourcesAsync(failures);

        if (failures.Count == 1)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }

        GC.SuppressFinalize(this);
    }

    private async Task StopClusterAsync(List<Exception> failures)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(ShutdownSeconds));
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(
            () => Cluster.StopAllSilosAsync(deadline.Token), failures);
    }

    private async Task DisposeOwnedResourcesAsync(List<Exception> failures)
    {
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => Cluster.DisposeAsync().AsTask(), failures);
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
        siloBuilder.Services.AddSerializer(serialization => serialization
            .AddAssembly(typeof(GrainRequestContextState).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
        siloBuilder.Services.AddSingleton(fixture.Database.Database);
        siloBuilder.Services.AddSingleton<ICommitCoordinator>(new EmbeddedCoordinator(fixture.Database.Database));
        siloBuilder.Services.AddSingleton(TimeProvider.System);
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
