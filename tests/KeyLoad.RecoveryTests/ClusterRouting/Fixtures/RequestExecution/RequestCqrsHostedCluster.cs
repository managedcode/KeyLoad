using System.Globalization;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Server;
using Orleans.TestingHost;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

/// <summary>Runs two complete production hosts using Orleans' native localhost membership provider.</summary>
internal sealed class RequestCqrsHostedCluster
{
    private const int SiloCount = 2;
    private const int ReplicaCount = 2;
    private const int SigningKeyBytes = 32;
    private readonly TestClusterPortAllocator ports = new();
    internal RequestCqrsHostedRuntimeFixture[] Nodes { get; }

    private RequestCqrsHostedCluster(IGrainRequestPhaseObserver? observer, Func<int, TimeProvider>? clocks,
        DatabaseLimits? limits, Func<int, IDatabaseAuthenticationExecutionObserver>? authenticationObservers)
    {
        var identity = "cqrs-cluster-" + Guid.NewGuid().ToString("N");
        var incarnation = Guid.NewGuid();
        var signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SigningKeyBytes));
        var (primaryPort, _) = ports.AllocateConsecutivePortPairs(SiloCount);
        Nodes = Enumerable.Range(0, SiloCount).Select(index => new RequestCqrsHostedRuntimeFixture(
            clock: clocks?.Invoke(index), observer: observer, limits: limits,
            authenticationExecutionObserver: authenticationObservers?.Invoke(index),
            clusterNode: new(identity, identity + "-silo-" + index.ToString(CultureInfo.InvariantCulture),
                incarnation, signingKey, primaryPort + index, primaryPort, SiloCount, ReplicaCount))).ToArray();
    }

    internal static async Task RunAsync(Func<RequestCqrsHostedCluster, Task> operation, CancellationToken token,
        IGrainRequestPhaseObserver? observer = null, Func<int, TimeProvider>? clocks = null, DatabaseLimits? limits = null,
        Func<int, IDatabaseAuthenticationExecutionObserver>? authenticationObservers = null)
    {
        var cluster = new RequestCqrsHostedCluster(observer, clocks, limits, authenticationObservers);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Task.WhenAll(cluster.Nodes.Select(node => node.InitializeAsync(token))).ConfigureAwait(false);
            await operation(cluster).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        foreach (var node in cluster.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => node.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        if (cluster.Nodes.All(static node => node.IsDisposalJoined))
        {
            ServerFailureObserver.Observe(cluster.ports.Dispose, failures);
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
