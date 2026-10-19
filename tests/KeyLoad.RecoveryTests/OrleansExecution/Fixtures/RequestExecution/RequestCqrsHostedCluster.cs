using System.Globalization;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace KeyLoad.RecoveryTests.Features.OrleansExecution;

/// <summary>Runs two complete production hosts using Orleans' execution localhost membership provider.</summary>
internal sealed class RequestCqrsHostedCluster : IAsyncDisposable
{
    private const int SiloCount = 2;
    private const int ReplicaCount = 2;
    private const int SigningKeyBytes = 32;
    private readonly Lock lifecycle = new();
    private readonly TestClusterPortAllocator ports = new();
    private readonly List<RequestCqrsHostedRuntimeFixture> ownedNodes = [];
    private Task? disposal;
    internal RequestCqrsHostedRuntimeFixture[] Nodes { get; private set; } = [];

    private RequestCqrsHostedCluster()
    {
    }

    private void Compose(IGrainRequestPhaseObserver? observer, Func<int, TimeProvider>? clocks,
        DatabaseLimits? limits, Func<int, IDatabaseAuthenticationExecutionObserver>? authenticationObservers,
        GrainRoutingOptions? routing, IGrainRequestPurposeObserver? purposeObserver,
        Action<int, IServiceCollection, NodeOptions>? configureServices)
    {
        var selectedRouting = RequestCqrsHostedProfile.ValidateRouting(routing).Value;
        var identity = "cqrs-cluster-" + Guid.NewGuid().ToString("N");
        var incarnation = Guid.NewGuid();
        var signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SigningKeyBytes));
        var (primaryPort, _) = ports.AllocateConsecutivePortPairs(SiloCount);
        foreach (var index in Enumerable.Range(0, SiloCount))
        {
            ownedNodes.Add(new RequestCqrsHostedRuntimeFixture(
                clock: clocks?.Invoke(index), routing: selectedRouting, observer: observer,
                purposeObserver: purposeObserver, limits: limits,
                authenticationExecutionObserver: authenticationObservers?.Invoke(index),
                configureServices: configureServices is null ? null : (services, node) => configureServices(index, services, node),
                clusterNode: new(identity, identity + "-silo-" + index.ToString(CultureInfo.InvariantCulture),
                    incarnation, signingKey, primaryPort + index, primaryPort, SiloCount, ReplicaCount)));
        }
        Nodes = ownedNodes.ToArray();
    }

    internal static async Task<RequestCqrsHostedCluster> CreateAsync(CancellationToken token,
        IGrainRequestPhaseObserver? observer = null, Func<int, TimeProvider>? clocks = null, DatabaseLimits? limits = null,
        Func<int, IDatabaseAuthenticationExecutionObserver>? authenticationObservers = null,
        GrainRoutingOptions? routing = null, IGrainRequestPurposeObserver? purposeObserver = null,
        Action<int, IServiceCollection, NodeOptions>? configureServices = null)
    {
        var cluster = new RequestCqrsHostedCluster();
        try
        {
            cluster.Compose(observer, clocks, limits, authenticationObservers, routing, purposeObserver, configureServices);
            await Task.WhenAll(cluster.Nodes.Select(node => node.InitializeAsync(token))).ConfigureAwait(false);
            return cluster;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            await ServerFailureObserver.ObserveAsync(() => cluster.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal static async Task RunAsync(Func<RequestCqrsHostedCluster, Task> operation, CancellationToken token,
        IGrainRequestPhaseObserver? observer = null, Func<int, TimeProvider>? clocks = null, DatabaseLimits? limits = null,
        Func<int, IDatabaseAuthenticationExecutionObserver>? authenticationObservers = null,
        GrainRoutingOptions? routing = null, IGrainRequestPurposeObserver? purposeObserver = null,
        Action<int, IServiceCollection, NodeOptions>? configureServices = null)
    {
        RequestCqrsHostedCluster? cluster = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            cluster = await CreateAsync(token, observer, clocks, limits, authenticationObservers,
                routing, purposeObserver, configureServices).ConfigureAwait(false);
            await operation(cluster).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (cluster is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => cluster.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            return new(disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        foreach (var node in ownedNodes)
        {
            await ServerFailureObserver.ObserveAsync(() => node.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        if (ownedNodes.All(static node => node.IsDisposalJoined))
        {
            try
            {
                ports.Dispose();
            }
            catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal Task StopAsync(CancellationToken token)
        => Task.WhenAll(Nodes.Select(node => node.StopAsync(token)));

    internal async Task RestartAsync(CancellationToken token)
    {
        await StopAsync(token).ConfigureAwait(false);
        await Task.WhenAll(Nodes.Select(node => node.RestartAsync(token))).ConfigureAwait(false);
    }

    internal void RetainRoots()
    {
        foreach (var node in Nodes)
        {
            node.RetainRoot();
        }
    }
}
