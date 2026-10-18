using System.Globalization;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Server;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Runs two complete production hosts using Orleans' native localhost membership provider.</summary>
internal sealed class RequestCqrsHostedCluster
{
    private const int SiloCount = 2;
    private const int ReplicaCount = 2;
    private const int SigningKeyBytes = 32;
    private readonly TestClusterPortAllocator ports = new();
    internal RequestCqrsHostedRuntimeFixture[] Nodes { get; }

    private RequestCqrsHostedCluster(IGrainRequestPhaseObserver? observer, Func<int, TimeProvider>? clocks,
        DatabaseLimits? limits)
    {
        var identity = "cqrs-cluster-" + Guid.NewGuid().ToString("N");
        var incarnation = Guid.NewGuid();
        var signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SigningKeyBytes));
        var (primaryPort, _) = ports.AllocateConsecutivePortPairs(SiloCount);
        Nodes = Enumerable.Range(0, SiloCount).Select(index => new RequestCqrsHostedRuntimeFixture(
            clock: clocks?.Invoke(index), observer: observer, limits: limits,
            clusterNode: new(identity, identity + "-silo-" + index.ToString(CultureInfo.InvariantCulture),
                incarnation, signingKey, primaryPort + index, primaryPort, SiloCount, ReplicaCount))).ToArray();
    }

    internal static async Task RunAsync(Func<RequestCqrsHostedCluster, Task> operation, CancellationToken token,
        IGrainRequestPhaseObserver? observer = null, Func<int, TimeProvider>? clocks = null, DatabaseLimits? limits = null)
    {
        var cluster = new RequestCqrsHostedCluster(observer, clocks, limits);
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
}
