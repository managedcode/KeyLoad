using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Comparisons.Targets;
using StackExchange.Redis;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class RedisNativeReadinessRegressionConnections : IAsyncDisposable
{
    private const string MissingConnection = "RedisNativeReadinessConnectionMissing";
    private readonly List<ConnectionMultiplexer> connections = [];

    internal ConnectionMultiplexer Primary => connections[0];
    internal ConnectionMultiplexer[] Replicas => connections.Skip(1).ToArray();
    internal EndPoint[] ReplicaEndpoints => connections.Skip(1)
        .Select(connection => RedisNativeProtocol.ConfiguredEndpoint(connection, MissingConnection)).ToArray();

    internal async Task ConnectAsync(DistributedApplication app, IEnumerable<RedisResource> resources, CancellationToken token)
    {
        foreach (var resource in resources.OrderBy(resource => resource.Name, StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            var connectionString = await app.GetConnectionStringAsync(resource.Name, token)
                ?? throw new InvalidOperationException(MissingConnection);
            var options = RedisReplicaProof.CreateOptions(connectionString);
            // Await native connection completion before cancellation so this owner can always dispose its result.
            connections.Add(await ConnectionMultiplexer.ConnectAsync(options));
            token.ThrowIfCancellationRequested();
        }
    }

    public async ValueTask DisposeAsync()
    {
        var originalDisposals = connections.Select(connection => connection.DisposeAsync().AsTask()).ToArray();
        await Task.WhenAll(originalDisposals);
    }
}
